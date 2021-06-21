using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CaptureEncoder
{
    public sealed class MyAudioGraphPlayer
    {

        public AudioGraph audioGraph;
        public MyAudioGraphPlayer()
        {



        }





        /// <summary>
        /// 初始化音频图
        /// </summary>
        /// <returns></returns>
        public async Task InitAudioGraph()
        {
            AudioGraphSettings settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media);
            CreateAudioGraphResult result = await AudioGraph.CreateAsync(settings);
            if (result.Status != AudioGraphCreationStatus.Success)
            {
                Debug.WriteLine("AudioGraph creation error: " + result.Status.ToString());
            }
            audioGraph = result.Graph;
            audioGraph.UnrecoverableErrorOccurred += async (sender, args) =>
            {
                if (sender == audioGraph && args.Error != AudioGraphUnrecoverableError.None)
                {
                    Debug.WriteLine("The audio graph encountered and unrecoverable error.");
                    audioGraph.Stop();
                    audioGraph.Dispose();
                    await InitAudioGraph();
                }
            };
        }





        #region 收起

        AudioDeviceInputNode deviceInputNode;

        private async Task CreateDeviceInputNode()
        {
            // Create a device output node
            CreateAudioDeviceInputNodeResult result = await audioGraph.CreateDeviceInputNodeAsync(Windows.Media.Capture.MediaCategory.Media);

            if (result.Status != AudioDeviceNodeCreationStatus.Success)
            {
                // Cannot create device output node
                //ShowErrorMessage(result.Status.ToString());
                return;
            }

            deviceInputNode = result.DeviceInputNode;
        }



        #endregion




        public AudioDeviceOutputNode deviceOutputNode;
        public async Task<AudioDeviceOutputNode> CreateDeviceOutputNode()
        {
            // Create a device output node
            CreateAudioDeviceOutputNodeResult result = await audioGraph.CreateDeviceOutputNodeAsync();

            if (result.Status != AudioDeviceNodeCreationStatus.Success)
            {
                // Cannot create device output node
                //ShowErrorMessage(result.Status.ToString());
                return null;
            }

            return result.DeviceOutputNode;
        }


        private async Task<AudioFileInputNode> CreateFileInputNode()
        {
            if (audioGraph == null)
                return null;

            FileOpenPicker filePicker = new FileOpenPicker();
            filePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            filePicker.FileTypeFilter.Add(".mp3");
            filePicker.FileTypeFilter.Add(".wav");
            filePicker.FileTypeFilter.Add(".wma");
            filePicker.FileTypeFilter.Add(".m4a");
            filePicker.ViewMode = PickerViewMode.Thumbnail;
            StorageFile file = await filePicker.PickSingleFileAsync();

            // File can be null if cancel is hit in the file picker
            if (file == null)
            {
                return null;
            }
            CreateAudioFileInputNodeResult result = await audioGraph.CreateFileInputNodeAsync(file);

            if (result.Status != AudioFileNodeCreationStatus.Success)
            {
                //ShowErrorMessage(result.Status.ToString());
            }

            return result.FileInputNode;
        }






        public MediaSourceAudioInputNode mediaSourceInputNode;

        public async Task CreateMediaSourceInputNode(System.Uri contentUri)
        {
            if (audioGraph == null)
                return;

            var adaptiveMediaSourceResult = await AdaptiveMediaSource.CreateFromUriAsync(contentUri);
            if (adaptiveMediaSourceResult.Status != AdaptiveMediaSourceCreationStatus.Success)
            {
                Debug.WriteLine("Failed to create AdaptiveMediaSource");



                return;
            }

            var mediaSource = MediaSource.CreateFromAdaptiveMediaSource(adaptiveMediaSourceResult.MediaSource);
            CreateMediaSourceAudioInputNodeResult mediaSourceAudioInputNodeResult =
                await audioGraph.CreateMediaSourceAudioInputNodeAsync(mediaSource);

            if (mediaSourceAudioInputNodeResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
            {
                switch (mediaSourceAudioInputNodeResult.Status)
                {
                    case MediaSourceAudioInputNodeCreationStatus.FormatNotSupported:
                        Debug.WriteLine("The MediaSource uses an unsupported format");
                        break;
                    case MediaSourceAudioInputNodeCreationStatus.NetworkError:
                        Debug.WriteLine("The MediaSource requires a network connection and a network-related error occurred");
                        break;
                    case MediaSourceAudioInputNodeCreationStatus.UnknownFailure:
                    default:
                        Debug.WriteLine("An unknown error occurred while opening the MediaSource");
                        break;
                }
                return;
            }

            mediaSourceInputNode = mediaSourceAudioInputNodeResult.Node;


            mediaSourceInputNode.MediaSourceCompleted += (s, e) =>
            {
                audioGraph.Stop();

            };
        }



        private MediaSource mediaSource;
        public async Task<MediaSourceAudioInputNode> CreateMediaSourceInputNode2(Uri contentUri)
        {
            if (audioGraph == null)
                return null;

            mediaSource = MediaSource.CreateFromUri(contentUri);
            CreateMediaSourceAudioInputNodeResult mediaSourceAudioInputNodeResult =
                await audioGraph.CreateMediaSourceAudioInputNodeAsync(mediaSource);

            if (mediaSourceAudioInputNodeResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
            {
                switch (mediaSourceAudioInputNodeResult.Status)
                {
                    case MediaSourceAudioInputNodeCreationStatus.FormatNotSupported:
                        Debug.WriteLine("The MediaSource uses an unsupported format");
                        break;
                    case MediaSourceAudioInputNodeCreationStatus.NetworkError:
                        Debug.WriteLine("The MediaSource requires a network connection and a network-related error occurred");
                        break;
                    case MediaSourceAudioInputNodeCreationStatus.UnknownFailure:
                    default:
                        Debug.WriteLine("An unknown error occurred while opening the MediaSource");
                        break;
                }
                return null;
            }

            return mediaSourceAudioInputNodeResult.Node;


            //mediaSourceInputNode.MediaSourceCompleted += (s, e) => {
            //    audioGraph.Stop();

            //};
        }



        private async Task CreateFileOutputNode()
        {
            FileSavePicker saveFilePicker = new FileSavePicker();
            saveFilePicker.FileTypeChoices.Add("Pulse Code Modulation", new List<string>() { ".wav" });
            saveFilePicker.FileTypeChoices.Add("Windows Media Audio", new List<string>() { ".wma" });
            saveFilePicker.FileTypeChoices.Add("MPEG Audio Layer-3", new List<string>() { ".mp3" });
            saveFilePicker.SuggestedFileName = "New Audio Track";
            StorageFile file = await saveFilePicker.PickSaveFileAsync();

            // File can be null if cancel is hit in the file picker
            if (file == null)
            {
                return;
            }

            Windows.Media.MediaProperties.MediaEncodingProfile mediaEncodingProfile;
            switch (file.FileType.ToString().ToLowerInvariant())
            {
                case ".wma":
                    mediaEncodingProfile = MediaEncodingProfile.CreateWma(AudioEncodingQuality.High);
                    break;
                case ".mp3":
                    mediaEncodingProfile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
                    break;
                case ".wav":
                    mediaEncodingProfile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
                    break;
                default:
                    throw new ArgumentException();
            }


            // Operate node at the graph format, but save file at the specified format
            CreateAudioFileOutputNodeResult result = await audioGraph.CreateFileOutputNodeAsync(file, mediaEncodingProfile);

            if (result.Status != AudioFileNodeCreationStatus.Success)
            {
                // FileOutputNode creation failed
                //ShowErrorMessage(result.Status.ToString());
                return;
            }

            //fileOutputNode = result.FileOutputNode;
        }





        public AudioFrameOutputNode frameOutputNode;

        public AudioFrameInputNode frameInputNode;

        private void CreateFrameInputNode()
        {
            // Create the FrameInputNode at the same format as the graph, except explicitly set mono.
            AudioEncodingProperties nodeEncodingProperties = audioGraph.EncodingProperties;
            nodeEncodingProperties.ChannelCount = 1;
            frameInputNode = audioGraph.CreateFrameInputNode(nodeEncodingProperties);

            // Initialize the Frame Input Node in the stopped state
            frameInputNode.Stop();

            // Hook up an event handler so we can start generating samples when needed
            // This event is triggered when the node is required to provide data
            frameInputNode.QuantumStarted += (s, args) =>
            {

                uint numSamplesNeeded = (uint)args.RequiredSamples;

                if (numSamplesNeeded != 0)
                {
                    //AudioFrame audioData = GenerateAudioData(numSamplesNeeded);
                    //frameInputNode.AddFrame(audioData);
                }
            };
        }



    }

}
