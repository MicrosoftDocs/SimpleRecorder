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
            // create AudioGraph
            AudioGraphSettings settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media);
            settings.QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency;
            var outputDevices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            settings.PrimaryRenderDevice = outputDevices[0];
            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status != AudioGraphCreationStatus.Success)
            {
                Debug.WriteLine("AudioGraph creation error: " + result.Status.ToString());
                return;
            }
            _audioGraph = result.Graph;
            
            _audioGraph.UnrecoverableErrorOccurred += (sender,e) => {
                sender.Dispose();
            };
            //_audioGraph.QuantumStarted += _audioGraph_QuantumStarted;

            // create device output
            var deviceOutputResult = await _audioGraph.CreateDeviceOutputNodeAsync();
            if (deviceOutputResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                Debug.WriteLine("Cannot create device output node");
                return;
            }
            _deviceOutputNode= deviceOutputResult.DeviceOutputNode;
            
            // create frame output
            _frameOutputNode = _audioGraph.CreateFrameOutputNode();
            //_frameOutputNode.ConsumeInput = true;
           
            //_frameOutputNode.Start();
            // create device input
            var deviceInputResult = await _audioGraph.CreateDeviceInputNodeAsync(MediaCategory.Other);
            if (deviceInputResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                Debug.WriteLine($"Audio Device Input unavailable because {deviceInputResult.Status.ToString()}");
                
                return;
            }
            _deviceInputNode = deviceInputResult.DeviceInputNode;

            _deviceInputNode.AddOutgoingConnection(_deviceOutputNode);
            //_deviceInputNode.AddOutgoingConnection(_frameOutputNode);

            //mp3File = await KnownFolders.VideosLibrary.CreateFileAsync("temp.mp3", CreationCollisionOption.ReplaceExisting);
            //var fileOutputNodeResult=await _audioGraph.CreateFileOutputNodeAsync(mp3File, MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High));
            //if (fileOutputNodeResult.Status != AudioFileNodeCreationStatus.Success)
            //{
            //    Debug.WriteLine($"Audio File output unavailable because {deviceInputResult.Status.ToString()}");

            //    return;
            //}

            //_fileOutputNode = fileOutputNodeResult.FileOutputNode;


            //





            //_deviceInputNode.AddOutgoingConnection(_fileOutputNode);

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
                    await CreateAudioObjects();
                    _audioDescriptor = new AudioStreamDescriptor(_audioGraph.EncodingProperties);
                    _mediaStreamSource.AddStreamDescriptor(_audioDescriptor);


                    encodingProfile.Audio = MediaEncodingProfile.CreateFlac(AudioEncodingQuality.Low).Audio;


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

            // audio 


            // Create our MediaStreamSource
            _mediaStreamSource = new MediaStreamSource(_videoDescriptor);
            _mediaStreamSource.BufferTime = TimeSpan.FromSeconds(0);
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

        TimeSpan delay=new TimeSpan(0);
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

                            var timeStamp = frame.SystemRelativeTime;
                            //Debug.WriteLine($"video:{timeStamp.TotalMilliseconds}");
                            //_time = timeStamp;
                            var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, timeStamp);
                            args.Request.Sample = sample;
                        }
                    }
                    else if (args.Request.StreamDescriptor.GetType() == typeof(AudioStreamDescriptor))
                    {
                        var request = args.Request;

                        var deferal = request.GetDeferral();

                        var frame = GetFrameAsync();


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

                            var stamp = time_start + frame.RelativeTime.GetValueOrDefault();
                            var duration = frame.Duration.GetValueOrDefault();

                            var sample = MediaStreamSample.CreateFromBuffer(data_buffer, stamp);
                            sample.Duration = duration;// frame.Duration.GetValueOrDefault();
                            sample.KeyFrame = true;
                            
                            if (sample.Discontinuous)
                            {
                                Debug.WriteLine("lost sample");
                                sample.Discontinuous = false;
                            }
                            //Debug.WriteLine($"audio:{stamp.TotalMilliseconds}duration:{sample.Duration.TotalMilliseconds}");

                            request.Sample = sample;
                            //Debug.WriteLine($"bytesize:{capacityInBytes}time:{frame.Duration.GetValueOrDefault().TotalMilliseconds}");
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



        AudioFrame GetFrameAsync() {
            var frame = _frameOutputNode.GetFrame();
            if (frame.Duration.GetValueOrDefault().TotalSeconds != 0)
            {
                return frame;

            }
            else
            {
                Debug.Write("Delay");
                Task.Delay(5);
                return GetFrameAsync();
            }
        }




        TimeSpan time_start=new TimeSpan();
        private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            MediaStreamSourceStartingRequest request = args.Request;
            

            using (var frame = _frameGenerator.WaitForNewFrame())
            {
                time_start = frame.SystemRelativeTime;
                request.SetActualStartPosition(frame.SystemRelativeTime);
                
            }
            _audioGraph?.Start();
            using (var audioFrame = _frameOutputNode.GetFrame())
            {
                time_start= time_start-audioFrame.RelativeTime.GetValueOrDefault();
            }
            //if ((request.StartPosition != null))
            //{
            //    UInt64 sampleOffset = (UInt64)request.StartPosition.Value.Ticks / (UInt64)sampleDuration.Ticks;
            //    timeOffset = new TimeSpan((long)sampleOffset * sampleDuration.Ticks);
            //    byteOffset = sampleOffset * sampleSize;
            //    Debug.WriteLine($"timeOffset:{timeOffset.TotalMilliseconds}ms");
            //}









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
        private AudioDeviceOutputNode _deviceOutputNode;
        private AudioFileOutputNode _fileOutputNode; 
        private const UInt32 sampleSize = 960;
        private TimeSpan sampleDuration = TimeSpan.FromMilliseconds(10);
        private InMemoryRandomAccessStream _memoryStream = new InMemoryRandomAccessStream();
        private IRandomAccessStream _fileStream;

        unsafe private void _audioGraph_QuantumStarted(AudioGraph sender, object args)
        {
            AudioFrame frame = _frameOutputNode?.GetFrame();

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

                WriteIntoMemory(data_buffer);
                //Debug.WriteLine($"bytesize:{capacityInBytes}time:{frame.Duration.GetValueOrDefault().TotalMilliseconds}");
            }


        }


        public async void WriteIntoMemory(IBuffer buffer)
        {

            var x = await _memoryStream.WriteAsync(buffer);
        }

    }
}
