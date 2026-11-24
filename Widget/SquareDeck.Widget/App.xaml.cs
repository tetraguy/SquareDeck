using System;
using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SquareDeck.Widget
{
    /// <summary>
    /// Game Bar starts the widget with an ms-gamebarwidget activation (as in Microsoft's Game Bar widget samples).
    /// Opened from Start instead, the same page shows in an ordinary window, which is handy for trying it out.
    /// </summary>
    sealed partial class App : Application
    {
        private XboxGameBarWidget _widget;

        public App()
        {
            InitializeComponent();
            // Never take Game Bar down with us: anything unexpected is swallowed.
            UnhandledException += (s, e) => e.Handled = true;
            Suspending += OnSuspending;
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            XboxGameBarWidgetActivatedEventArgs widgetArgs = null;
            if (args.Kind == ActivationKind.Protocol && args is IProtocolActivatedEventArgs protocol && protocol.Uri.Scheme.Equals("ms-gamebarwidget"))
                widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
            if (widgetArgs == null || !widgetArgs.IsLaunchActivation) return;

            var frame = new Frame();
            Window.Current.Content = frame;
            if (widgetArgs.AppExtensionId == "SquareDeckWidget")
            {
                _widget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, frame);
                frame.Navigate(typeof(WidgetPage), _widget);
                Window.Current.Closed += (s, e) => _widget = null;
            }
            Window.Current.Activate();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                frame = new Frame();
                Window.Current.Content = frame;
            }
            if (frame.Content == null) frame.Navigate(typeof(WidgetPage), null);
            Window.Current.Activate();
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}
