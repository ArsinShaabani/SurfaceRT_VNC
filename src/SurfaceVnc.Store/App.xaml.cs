using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SurfaceVnc.Store
{
    /// <summary>Application entry point (the manifest points at <c>SurfaceVnc.Store.App</c>).</summary>
    public sealed partial class App : Application
    {
        private Frame _rootFrame;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _rootFrame = Window.Current.Content as Frame;

            if (_rootFrame == null)
            {
                _rootFrame = new Frame();
                Window.Current.Content = _rootFrame;
            }

            if (_rootFrame.Content == null)
                _rootFrame.Navigate(typeof(MainPage), args.Arguments);

            Window.Current.Activate();
        }
    }
}
