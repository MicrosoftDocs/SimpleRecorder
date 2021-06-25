// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CaptureEncoder
{
    public sealed class Encoder : IDisposable
    {
        public Encoder(IDirect3DDevice device, GraphicsCaptureItem item)
        {
            _device = device;
            _captureItem = item;
            _isRecording = false;

            CreateMediaObjects();


        }

        private async Task CreateAudioObjects()
        {
            AudioGraphSettings settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media);
            settings.QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency;
            // create AudioGraph
            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status != AudioGraphCreationStatus.Success)
            {
                Debug.WriteLine("AudioGraph creation error: " + result.Status.ToString());
                return;
            }
            _audioGraph = result.Graph;

            // create device input _ a microphone
            var deviceInputResult = await _audioGraph.CreateDeviceInputNodeAsync(MediaCategory.Other);
            if (deviceInputResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                Debug.WriteLine($"Audio Device Input unavailable because {deviceInputResult.Status.ToString()}");

                return;
            }
            _deviceInputNode = deviceInputResult.DeviceInputNode;

            // create output frame 
            _frameOutputNode = _audioGraph.CreateFrameOutputNode();
            // increase volume of input
            _deviceInputNode.OutgoingGain = 10;
            _deviceInputNode.AddOutgoingConnection(_frameOutputNode);

        }
     

        public IAsyncAction EncodeAsync(IRandomAccessStream stream, uint width, uint height, uint bitrateInBps, uint frameRate)
        {
            return EncodeInternalAsync(stream, width, height, bitrateInBps, frameRate).AsAsyncAction();
        }

        private async Task EncodeInternalAsync(IRandomAccessStream stream, uint width, uint height, uint bitrateInBps, uint frameRate)
        {
            if (!_isRecording)
            {
                _isRecording = true;

                _frameGenerator = new CaptureFrameWait(
                    _device,
                    _captureItem,
                    _captureItem.Size);

                using (_frameGenerator)
                {
                    var encodingProfile = new MediaEncodingProfile();
                    encodingProfile.Container.Subtype = MediaEncodingSubtypes.Mpeg4;
                    encodingProfile.Video.Subtype = MediaEncodingSubtypes.H264;
                    encodingProfile.Video.Width = width;
                    encodingProfile.Video.Height = height;
                    encodingProfile.Video.Bitrate = bitrateInBps;
                    encodingProfile.Video.FrameRate.Numerator = frameRate;
                    encodingProfile.Video.FrameRate.Denominator = 1;
                    encodingProfile.Video.PixelAspectRatio.Numerator = 1;
                    encodingProfile.Video.PixelAspectRatio.Denominator = 1;
                    // Describe audio input
                    encodingProfile.Audio = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.Low).Audio;


                    // create audio graph
                    if (_audioGraph==null)
                    {
                        await CreateAudioObjects();
                    }

                    // add audio support
                    _audioDescriptor = new AudioStreamDescriptor(_audioGraph.EncodingProperties);
                    _mediaStreamSource.AddStreamDescriptor(_audioDescriptor);


                    var transcode = await _transcoder.PrepareMediaStreamSourceTranscodeAsync(_mediaStreamSource, stream, encodingProfile);
                    await transcode.TranscodeAsync();
                }
            }
        }

        public void Dispose()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;

            if (!_isRecording)
            {
                DisposeInternal();
            }

            _isRecording = false;            
        }

        private  void DisposeInternal()
        {
            _frameGenerator.Dispose();

        }

        private void CreateMediaObjects()
        {

            // Create our encoding profile based on the size of the item
            int width = _captureItem.Size.Width;
            int height = _captureItem.Size.Height;

            // Describe our input: uncompressed BGRA8 buffers
            var videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
            _videoDescriptor = new VideoStreamDescriptor(videoProperties);

            // Create our MediaStreamSource
            _mediaStreamSource = new MediaStreamSource(_videoDescriptor);
            _mediaStreamSource.CanSeek = true;
            _mediaStreamSource.BufferTime = TimeSpan.FromMilliseconds(0);
            _mediaStreamSource.Starting += OnMediaStreamSourceStarting;
            _mediaStreamSource.SampleRequested += OnMediaStreamSourceSampleRequested;
            _mediaStreamSource.Closed += (s,e) => {
                Debug.WriteLine("Stop AudioGraph");
                _audioGraph?.Stop();

            };

            // Create our transcoder
            _transcoder = new MediaTranscoder();
            _transcoder.HardwareAccelerationEnabled = true;
        }

       
        unsafe private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            if (_isRecording && !_closed)
            {
                try
                {

                    if (args.Request.StreamDescriptor.GetType() == typeof(VideoStreamDescriptor))
                    {
                        // Request Video
                        using (var frame = _frameGenerator.WaitForNewFrame())
                        {
                            if (frame == null)
                            {
                                args.Request.Sample = null;
                                DisposeInternal();
                                return;
                            }
                            var timeStamp = frame.SystemRelativeTime- timeOffset;
                            var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, timeStamp);
                            args.Request.Sample = sample;
                        }
                    }
                    else if (args.Request.StreamDescriptor.GetType() == typeof(AudioStreamDescriptor))
                    {
                        var request = args.Request;

                        var deferal = request.GetDeferral();

                        var frame = _frameOutputNode.GetFrame();
                        if (frame.Duration.GetValueOrDefault().TotalSeconds==0)
                        {
                            args.Request.Sample = null;
                            return;
                        }
                        using (AudioBuffer buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
                        using (IMemoryBufferReference reference = buffer.CreateReference())
                        {
                            byte* dataInBytes;
                            uint capacityInBytes;
                            // Get the buffer from the AudioFrame
                            ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacityInBytes);
                            byte[] bytes = new byte[capacityInBytes];
                            Marshal.Copy((IntPtr)dataInBytes, bytes, 0, (int)capacityInBytes);
                            var data_buffer = WindowsRuntimeBufferExtensions.AsBuffer(bytes, 0, (int)capacityInBytes);

                            var stamp = frame.RelativeTime.GetValueOrDefault();
                            var duration = frame.Duration.GetValueOrDefault();

                            var sample = MediaStreamSample.CreateFromBuffer(data_buffer, stamp);
                            sample.Duration = duration;
                            sample.KeyFrame = true;

                            request.Sample = sample;
                            
                        }

                        deferal.Complete();

                    }

                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    Debug.WriteLine(e.StackTrace);
                    Debug.WriteLine(e);
                    args.Request.Sample = null;
                    DisposeInternal();
                }
            }
            else
            {
                args.Request.Sample = null;
                DisposeInternal();
            }
        }


        
        private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            MediaStreamSourceStartingRequest request = args.Request;

            using (var frame = _frameGenerator.WaitForNewFrame())
            {
                timeOffset = frame.SystemRelativeTime;
                //request.SetActualStartPosition(frame.SystemRelativeTime);
            }
            _audioGraph?.Start();
            using (var audioFrame = _frameOutputNode.GetFrame())
            {
                timeOffset = timeOffset + audioFrame.RelativeTime.GetValueOrDefault();
            }
        }

        private IDirect3DDevice _device;

        private GraphicsCaptureItem _captureItem;
        private CaptureFrameWait _frameGenerator;

        private VideoStreamDescriptor _videoDescriptor;
        private AudioStreamDescriptor _audioDescriptor;
        private MediaStreamSource _mediaStreamSource;
        private MediaTranscoder _transcoder;
        private bool _isRecording;
        private bool _closed = false;

        // audio graph and nodes
        private AudioGraph _audioGraph;
        private AudioDeviceInputNode _deviceInputNode;
        private AudioFrameOutputNode _frameOutputNode;
        private TimeSpan timeOffset = new TimeSpan();

    }
}
