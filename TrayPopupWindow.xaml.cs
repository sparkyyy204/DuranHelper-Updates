using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace FSB_helper_C__
{
    public partial class TrayPopupWindow : Window
    {
        public event Action RestoreRequested;
        public event Action CloseRequested;
        private bool _isOpen = false;

        public TrayPopupWindow()
        {
            InitializeComponent();
            this.Opacity = 0;
            this.Left = -9999;
            this.Top = -9999;
            this.Loaded += (s, e) => { this.Opacity = 0; };
        }

        public void ShowAtPosition(double x, double y)
        {
            // If already open, close it (toggle behavior)
            if (_isOpen)
            {
                MoveAway();
                return;
            }

            _isOpen = true;

            // Clear lingering animations and set to invisible
            this.BeginAnimation(OpacityProperty, null);
            scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            this.Opacity = 0;
            scaleTransform.ScaleY = 0.85;
            scaleTransform.ScaleX = 0.95;

            // Position above the tray icon area
            double targetLeft = x - this.Width / 2;
            double targetTop = y - this.Height;

            // Keep on screen
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)x, (int)y));
            var wa = screen.WorkingArea;
            if (targetLeft < wa.Left) targetLeft = wa.Left + 4;
            if (targetLeft + this.Width > wa.Right) targetLeft = wa.Right - this.Width - 4;
            if (targetTop < wa.Top) targetTop = wa.Top + 4;
            if (targetTop + this.Height > wa.Bottom) targetTop = wa.Bottom - this.Height - 4;

            this.Left = targetLeft;
            this.Top = targetTop;

            // Show window (first time) or just activate
            if (!this.IsVisible) this.Show();
            this.Activate();

            // Defer animation to next render frame
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () => {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var scaleYAnim = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(150))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var scaleXAnim = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(150))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

                this.BeginAnimation(OpacityProperty, fadeIn);
                scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnim);
                scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnim);
            });
        }

        private async void MoveAway()
        {
            if (!_isOpen) return;
            _isOpen = false;

            var fadeOut = new DoubleAnimation(this.Opacity, 0, TimeSpan.FromMilliseconds(80));
            this.BeginAnimation(OpacityProperty, fadeOut);
            await System.Threading.Tasks.Task.Delay(90);

            this.BeginAnimation(OpacityProperty, null);
            this.Opacity = 0;
            this.Left = -9999;
            this.Top = -9999;
        }

        private void BtnShow_Click(object sender, RoutedEventArgs e)
        {
            _isOpen = false;
            this.BeginAnimation(OpacityProperty, null);
            this.Opacity = 0;
            this.Left = -9999;
            RestoreRequested?.Invoke();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _isOpen = false;
            this.BeginAnimation(OpacityProperty, null);
            this.Opacity = 0;
            this.Left = -9999;
            CloseRequested?.Invoke();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            MoveAway();
        }
    }
}
