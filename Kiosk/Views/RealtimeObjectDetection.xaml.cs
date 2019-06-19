// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Cognitive Services: http://www.microsoft.com/cognitive
// 
// Microsoft Cognitive Services Github:
// https://github.com/Microsoft/Cognitive
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 

using IntelligentKioskSample.Controls;
using IntelligentKioskSample.Models;
using IntelligentKioskSample.Views.CustomVision;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using DJI.WindowsSDK;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.AI.MachineLearning;
using Windows.Storage.Streams;
using DJIVideoParser;
using System.Diagnostics;
using System.Threading;
//using DJIVideoParser;

namespace IntelligentKioskSample.Views
{
    [KioskExperience(Title = "Realtime Object Detection", ImagePath = "ms-appx:/Assets/RealtimeObjectDetection.png", ExperienceType = ExperienceType.Kiosk)]
    public sealed partial class RealtimeObjectDetection : Page
    {
        private readonly int ObjectDetectionModelInputSize = 416;
        private readonly float MinProbabilityValue = 0.6f;

        private string[] allModelObjects;
        private ObjectDetection objectDetectionModel;
        private bool isModelLoadedSuccessfully = false;
        private DJIVideoParser.Parser videoParser;
        private SemaphoreSlim _slim = new SemaphoreSlim(1);

        public ObservableCollection<CustomVisionModelData> Projects { get; set; } = new ObservableCollection<CustomVisionModelData>();

        public RealtimeObjectDetection()
        {
            this.InitializeComponent();

            RegisterDJISDK();
        }

        private void RegisterDJISDK() {
            try
            {
                DJISDKManager.Instance.SDKRegistrationStateChanged += Instance_SDKRegistrationEvent;

                // Replace with your registered DJI App Key as per https://developer.dji.com/windows-sdk/documentation/quick-start/index.html. 
                // Make sure your App Key matched your application's package name on DJI developer center.
                DJISDKManager.Instance.RegisterApp(SettingsHelper.DJIAppplicationKey);
            }
            catch (Exception)
            {

                throw;
            }
        }

        private async void Instance_SDKRegistrationEvent(SDKRegistrationState state, SDKError resultCode)
        {
            if (resultCode == SDKError.NO_ERROR)
            {
                System.Diagnostics.Debug.WriteLine("Register app successfully.");
                
                //The product connection state will be updated when it changes here.
                DJISDKManager.Instance.ComponentManager.GetProductHandler(0).ProductTypeChanged += async delegate (object sender, ProductTypeMsg? value)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    {
                        if (value != null && value?.value != ProductType.UNRECOGNIZED)
                        {
                            System.Diagnostics.Debug.WriteLine("The Aircraft is connected now 1.");
                            //You can load/display your pages according to the aircraft connection state here.
                            await InitializeVideoFeedModule();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("The Aircraft is disconnected now.");
                            //You can hide your pages according to the aircraft connection state here, or show the connection tips to the users.
                        }
                    });
                };
                /*
                //If you want to get the latest product connection state manually, you can use the following code
                var productType = (await DJISDKManager.Instance.ComponentManager.GetProductHandler(0).GetProductTypeAsync()).value;
                if (productType != null && productType?.value != ProductType.UNRECOGNIZED)
                {
                    System.Diagnostics.Debug.WriteLine("The Aircraft is connected now 2.");
                    //You can load/display your pages according to the aircraft connection state here.
                    await InitializeVideoFeedModule();
                    
                }
                */
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Register SDK failed, the error is: ");
                System.Diagnostics.Debug.WriteLine(resultCode.ToString());
            }
        }
        
        private async Task InitializeVideoFeedModule()
        {
            System.Diagnostics.Debug.WriteLine("Init Video Feed");
            //Must in UI thread
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                System.Diagnostics.Debug.WriteLine("Init Video Feed on UI Thread");
                //Raw data and decoded data listener
                if (videoParser == null)
                {
                    try
                    {
                        videoParser = new DJIVideoParser.Parser();
                    }
                    catch(Exception e)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to init!");
                        return;
                    }
                    videoParser.Initialize(delegate (byte[] data)
                    {
                        //Note: This function must be called because we need DJI Windows SDK to help us to parse frame data.
                        return DJISDKManager.Instance.VideoFeeder.ParseAssitantDecodingInfo(0, data);
                    });

                    System.Diagnostics.Debug.WriteLine("Video parser ready");

                    //Set the swapChainPanel to display and set the decoded data callback.
                    videoParser.SetSurfaceAndVideoCallback(0, 0, swapChainPanel, ReceiveDecodedData);
                    DJISDKManager.Instance.VideoFeeder.GetPrimaryVideoFeed(0).VideoDataUpdated += (sender, bytes) =>
                        videoParser.PushVideoData(0, 0, bytes, bytes.Length);
                }
                //get the camera type and observe the CameraTypeChanged event.
                var type = await DJISDKManager.Instance.ComponentManager.GetCameraHandler(0, 0).GetCameraTypeAsync();
                this.videoParser.SetCameraSensor(AircraftCameraType.Mavic2Pro);
                System.Diagnostics.Debug.WriteLine("Set camera type for " + type.value?.value??"");
                await DJI.WindowsSDK.DJISDKManager.Instance.ComponentManager.GetCameraHandler(0, 0).SetCameraWorkModeAsync(new CameraWorkModeMsg { value = CameraWorkMode.SHOOT_PHOTO });
                System.Diagnostics.Debug.WriteLine("Live!");
            });
        }

        async void ReceiveDecodedData(byte[] data, int width, int height)
        {
            if (!await _slim.WaitAsync(0)) return;
            try
            {
                IBuffer buffer = data.AsBuffer();
                Debug.WriteLine($"ReceiveDecodedData  Width: {width} Height: {height}");
                using (SoftwareBitmap softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Rgba8, width, height))
                using (VideoFrame videoFrame = VideoFrame.CreateWithSoftwareBitmap(softwareBitmap))
                {
                    await ProcessFrameInternal(videoFrame);
                }
            }
            finally
            {
                _slim.Release();
            }
        }
        public async Task ProcessFrameInternal(VideoFrame videoFrame)
        {
            Canvas visualizationCanvas = this.FaceTrackingVisualizationCanvas;

            if (!isModelLoadedSuccessfully)
            {
                return;
            }

            try
            {
                using (SoftwareBitmap bitmapBuffer = new SoftwareBitmap(BitmapPixelFormat.Bgra8,
                    ObjectDetectionModelInputSize, ObjectDetectionModelInputSize, BitmapAlphaMode.Ignore))
                {
                    using (VideoFrame buffer = VideoFrame.CreateWithSoftwareBitmap(bitmapBuffer))
                    {
                        await videoFrame.CopyToAsync(buffer);

                        System.DateTime start = System.DateTime.Now;

                        IList<PredictionModel> predictions = await this.objectDetectionModel.PredictImageAsync(buffer);

                        double predictionTimeInMilliseconds = (System.DateTime.Now - start).TotalMilliseconds;

                        if (predictions.Count > 0) Debug.WriteLine("I saw " + string.Join(", ", predictions.Select(x => x.TagName)));

                        await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            this.ShowVisualization(visualizationCanvas, predictions);
                            this.fpsTextBlock.Text = predictionTimeInMilliseconds > 0 ? $"{Math.Round(1000 / predictionTimeInMilliseconds)} fps" : string.Empty;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                this.isModelLoadedSuccessfully = false;
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    await Util.GenericApiCallExceptionHandler(ex, "Failure processing frame");
                });
            }
        }


        private void CameraControl_CameraAspectRatioChanged(object sender, EventArgs e)
        {
            this.UpdateCameraHostSize();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            this.DataContext = this;
            EnterKioskMode();

            await this.LoadProjectsFromFile(e.Parameter as CustomVisionModelData);

            base.OnNavigatedTo(e);
        }

        private void EnterKioskMode()
        {
            ApplicationView view = ApplicationView.GetForCurrentView();
            if (!view.IsFullScreenMode)
            {
                view.TryEnterFullScreenMode();
            }
        }

        protected override async void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);
        }

        private async Task LoadProjectsFromFile(CustomVisionModelData preselectedProject = null)
        {
            try
            {
                this.Projects.Clear();
                List<CustomVisionModelData> prebuiltModelList = CustomVisionDataLoader.GetBuiltInModelData(CustomVisionProjectType.ObjectDetection) ?? new List<CustomVisionModelData>();
                foreach (CustomVisionModelData prebuiltModel in prebuiltModelList)
                {
                    this.Projects.Add(prebuiltModel);
                }

                List<CustomVisionModelData> customVisionModelList = await CustomVisionDataLoader.GetCustomVisionModelDataAsync(CustomVisionProjectType.ObjectDetection) ?? new List<CustomVisionModelData>();
                foreach (CustomVisionModelData customModel in customVisionModelList)
                {
                    this.Projects.Add(customModel);
                }

                CustomVisionModelData defaultProject = preselectedProject != null ? this.Projects.FirstOrDefault(x => x.Id.Equals(preselectedProject.Id)) : null;
                if (defaultProject != null)
                {
                    this.projectsComboBox.SelectedValue = defaultProject;
                }
                else
                {
                    this.projectsComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                await Util.GenericApiCallExceptionHandler(ex, "Failure loading projects");
            }
        }

        private void LoadSupportedClasses(CustomVisionModelData currentProject)
        {
            string modelLabelsPrefix = "Supported classes: ";
            this.modelObjects.Text = string.Empty;
            this.moreModelObjects.Text = string.Empty;

            this.allModelObjects = currentProject?.ClassLabels?.OrderBy(x => x).ToArray() ?? new string[] { };
            if (this.allModelObjects.Length > 2)
            {
                this.modelObjects.Text = $"{modelLabelsPrefix}{string.Join(", ", this.allModelObjects.Take(2))}";
                this.moreModelObjects.Text = $"and {allModelObjects.Length - 2} more"; ;
            }
            else
            {
                this.modelObjects.Text = modelLabelsPrefix + string.Join(", ", allModelObjects);
            }
        }

        private void OnShowAllSupportedClassesTapped(object sender, TappedRoutedEventArgs e)
        {
            this.allSupportedClassesListView.ItemsSource = new ObservableCollection<string>(this.allModelObjects);
            supportedClassesBox.ShowAt((TextBlock)sender);
        }

        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.UpdateCameraHostSize();
        }

        private void UpdateCameraHostSize()
        {
            Debug.WriteLine($"UpdateCameraHostSize  Width: {this.cameraHostGrid.ActualWidth} Height: {this.cameraHostGrid.ActualHeight}");
            this.cameraHostGrid.Width = this.cameraHostGrid.ActualHeight;
        }

        private async void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                this.isModelLoadedSuccessfully = false;
                if (this.projectsComboBox.SelectedValue is CustomVisionModelData currentProject)
                {
                    await LoadCurrentModelAsync(currentProject);
                }
            }
            finally
            {
                this.isModelLoadedSuccessfully = true;
            }
        }

        private async Task LoadCurrentModelAsync(CustomVisionModelData currentProject)
        {
            try
            {
                this.deleteBtn.Visibility = currentProject.IsPrebuiltModel ? Visibility.Collapsed : Visibility.Visible;
                LoadSupportedClasses(currentProject);
                StorageFile modelFile = await GetModelFileAsync(currentProject);
                this.objectDetectionModel = new ObjectDetection(this.allModelObjects, probabilityThreshold: MinProbabilityValue);
                await this.objectDetectionModel.Init(modelFile);
            }
            catch (Exception ex)
            {
                await Util.GenericApiCallExceptionHandler(ex, "Failure loading current project");
            }
        }

        private async Task<StorageFile> GetModelFileAsync(CustomVisionModelData customVisionModelData)
        {
            if (customVisionModelData.IsPrebuiltModel)
            {
                string modelPath = $"Assets\\{customVisionModelData.FileName}";
                return await Package.Current.InstalledLocation.GetFileAsync(modelPath);
            }
            else
            {
                StorageFolder onnxProjectDataFolder = await CustomVisionDataLoader.GetOnnxModelStorageFolderAsync(CustomVisionProjectType.ObjectDetection);
                return await onnxProjectDataFolder.GetFileAsync(customVisionModelData.FileName);
            }
        }

        private async void OnAddProjectClicked(object sender, RoutedEventArgs e)
        {
            await new MessageDialog("To add a new project here, please select one of your projects in the Custom Vision Setup page and use the ONNX Export feature.", "New project").ShowAsync();
        }

        private async void OnDeleteProjectClicked(object sender, RoutedEventArgs e)
        {
            await Util.ConfirmActionAndExecute("Delete project?", async () => { await DeleteProjectAsync(); });
        }

        private async Task DeleteProjectAsync()
        {
            CustomVisionModelData currentProject = this.projectsComboBox.SelectedValue as CustomVisionModelData;
            if (currentProject != null && !currentProject.IsPrebuiltModel)
            {
                // delete ONNX model file
                StorageFolder onnxProjectDataFolder = await CustomVisionDataLoader.GetOnnxModelStorageFolderAsync(CustomVisionProjectType.ObjectDetection);
                StorageFile modelFile = await onnxProjectDataFolder.GetFileAsync(currentProject.FileName);
                if (modelFile != null)
                {
                    await modelFile.DeleteAsync();
                }

                // update local file with custom models
                this.Projects.Remove(currentProject);
                List<CustomVisionModelData> updatedCustomModelList = this.Projects.Where(x => !x.IsPrebuiltModel).ToList();
                await CustomVisionDataLoader.SaveCustomVisionModelDataAsync(updatedCustomModelList, CustomVisionProjectType.ObjectDetection);

                this.projectsComboBox.SelectedIndex = 0;
            }
        }

        private void ShowVisualization(Canvas visualizationCanvas, IList<PredictionModel> detectedObjects)
        {
            visualizationCanvas.Children.Clear();

            Debug.WriteLine($"ShowVisualization canvas  Width: {visualizationCanvas.ActualWidth} Height: {visualizationCanvas.ActualHeight}");
            
            double canvasWidth = visualizationCanvas.ActualWidth;
            double canvasHeight = visualizationCanvas.ActualHeight;

            foreach (PredictionModel prediction in detectedObjects)
            {
                visualizationCanvas.Children.Add(
                    new Border
                    {
                        BorderBrush = new SolidColorBrush(Colors.Lime),
                        BorderThickness = new Thickness(2),
                        Margin = new Thickness(prediction.BoundingBox.Left * canvasWidth,
                                           prediction.BoundingBox.Top * canvasHeight, 0, 0),
                        Width = prediction.BoundingBox.Width * canvasWidth,
                        Height = prediction.BoundingBox.Height * canvasHeight,
                    });

                visualizationCanvas.Children.Add(
                    new Border
                    {
                        Width = 300,
                        Height = 40,
                        FlowDirection = FlowDirection.LeftToRight,
                        Margin = new Thickness(prediction.BoundingBox.Left * canvasWidth + prediction.BoundingBox.Width * canvasWidth - 300,
                                               prediction.BoundingBox.Top * canvasHeight - 40, 0, 0),

                        Child = new Border
                        {
                            Background = new SolidColorBrush(Colors.Lime),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Child =
                                new TextBlock
                                {
                                    Text = $"{prediction.TagName}",
                                    FontSize = 24,
                                    Foreground = new SolidColorBrush(Colors.Black),
                                    Margin = new Thickness(6, 0, 6, 0)
                                }
                        }
                    });
            }
        }
    }
}
