using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FSB_helper_C__
{
    public partial class DialogHost : UserControl
    {
        // ── Callbacks ──
        private Action _alertConfirmAction;
        private Action<string> _inputSubmitAction;
        private Func<string, string> _inputValidator;
        private int _inputMaxLength = 25;

        public Action OnInstructionClick { get; set; }

        public DialogHost()
        {
            InitializeComponent();
            cbSectionType.ItemsSource = new string[] {
                "1 колонка (построчно)",
                "2 колонки (столбики)",
                "Блокнот (Текст)"
            };
        }

        // ═══════════════════════════════════════
        //  INFO (одна кнопка ПОНЯТНО)
        // ═══════════════════════════════════════
        public void ShowInfo(string title, string message, bool isSuccess = false)
        {
            var brush = isSuccess ? (Brush)FindResource("GreenBrush") : (Brush)FindResource("RedBrush");
            var bgTint = isSuccess ? "#0f1c12" : "#1c0f12";

            WinInfoBg.BorderBrush = brush;
            lblInfoTitle.Foreground = isSuccess ? brush : (Brush)new SolidColorBrush(Colors.White);
            lblInfoLabel.Foreground = brush;
            lblInfoMsg.Foreground = brush;

            // Toggle visibility of specific elements
            lblInfoIconError.Visibility = isSuccess ? Visibility.Collapsed : Visibility.Visible;
            lblInfoIconSuccess.Visibility = isSuccess ? Visibility.Visible : Visibility.Collapsed;

            btnInfoOkError.Visibility = isSuccess ? Visibility.Collapsed : Visibility.Visible;
            btnInfoOkSuccess.Visibility = isSuccess ? Visibility.Visible : Visibility.Collapsed;

            // Update main shadow color based on state
            if (WinInfo.Children[0] is Border shadowBorder && shadowBorder.Effect is DropShadowEffect fx)
            {
                fx.Color = isSuccess ? (Color)ColorConverter.ConvertFromString("#2ea043") : (Color)ColorConverter.ConvertFromString("#ff7b72");
            }

            infoMsgBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgTint));
            infoMsgBorder.BorderBrush = brush;

            lblInfoTitle.Text = title;
            lblInfoMsg.Text = message;
            lblInfoLabel.Text = isSuccess ? "ОПЕРАЦИЯ ВЫПОЛНЕНА:" : "СИСТЕМНОЕ СООБЩЕНИЕ:";

            ShowDialog(WinInfo);
        }

        // ═══════════════════════════════════════
        //  ALERT (ДА/НЕТ → callback)
        // ═══════════════════════════════════════
        public void ShowAlert(string title, string message, Action onConfirm)
        {
            _alertConfirmAction = onConfirm;
            lblAlertTitle.Text = title;
            lblAlertMsg.Text = message;
            ShowDialog(WinAlert);
        }

        // ═══════════════════════════════════════
        //  INPUT (ввод текста → callback)
        // ═══════════════════════════════════════
        public void ShowInput(string title, Action<string> onSubmit, 
            string watermark = "", int maxLength = 25,
            Func<string, string> validator = null,
            bool showSectionType = false)
        {
            _inputSubmitAction = onSubmit;
            _inputValidator = validator;
            _inputMaxLength = maxLength;

            lblInputTitle.Text = title;
            txtInputField.Text = "";
            lblInputHint.Visibility = Visibility.Collapsed;
            lblInputHint.Text = "";

            if (!string.IsNullOrEmpty(watermark))
            {
                lblInputWatermark.Text = watermark;
                lblInputWatermark.Visibility = Visibility.Visible;
            }
            else
            {
                lblInputWatermark.Visibility = Visibility.Collapsed;
            }

            pnlSectionType.Visibility = showSectionType ? Visibility.Visible : Visibility.Collapsed;
            btnSectionInfo.Visibility = showSectionType ? Visibility.Visible : Visibility.Collapsed;
            if (showSectionType) {
                cbSectionType.SelectedIndex = 1;
            }

            ShowDialog(WinInputDlg);
            txtInputField.Focus();
        }

        /// <summary>Получить выбранный тип раздела (0=2col, 1=text, 2=1col)</summary>
        public int GetSectionTypeIndex() => cbSectionType.SelectedIndex;

        // chkHasPunishments removed: rows are now universal

        // ═══════════════════════════════════════
        //  CLOSE
        // ═══════════════════════════════════════
        public async void CloseDialog()
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            this.BeginAnimation(OpacityProperty, fade);
            await Task.Delay(160);
            this.Visibility = Visibility.Collapsed;
            WinInfo.Visibility = Visibility.Collapsed;
            WinAlert.Visibility = Visibility.Collapsed;
            WinInputDlg.Visibility = Visibility.Collapsed;
            _alertConfirmAction = null;
            _inputSubmitAction = null;
            _inputValidator = null;
        }

        /// <summary>Проверяет, открыт ли какой-нибудь диалог</summary>
        public bool IsOpen => this.Visibility == Visibility.Visible;

        // ═══════════════════════════════════════
        //  INTERNAL
        // ═══════════════════════════════════════
        private void ShowDialog(UIElement win)
        {
            WinInfo.Visibility = Visibility.Collapsed;
            WinAlert.Visibility = Visibility.Collapsed;
            WinInputDlg.Visibility = Visibility.Collapsed;

            win.Visibility = Visibility.Visible;
            this.Visibility = Visibility.Visible;
            this.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15));
            this.BeginAnimation(OpacityProperty, fade);
        }

        // ── Button handlers ──
        private void BtnSectionInfo_Click(object sender, RoutedEventArgs e) => OnInstructionClick?.Invoke();
        private void InfoOk_Click(object sender, RoutedEventArgs e) => CloseDialog();

        private async void AlertYes_Click(object sender, RoutedEventArgs e)
        {
            var action = _alertConfirmAction;
            CloseDialog();
            await Task.Delay(160);
            action?.Invoke();
        }

        private void AlertNo_Click(object sender, RoutedEventArgs e) => CloseDialog();

        private void InputOk_Click(object sender, RoutedEventArgs e)
        {
            string v = txtInputField.Text;

            if (string.IsNullOrWhiteSpace(v))
            {
                ShowInputError("Пожалуйста, введите значение!");
                return;
            }

            if (v.Length > _inputMaxLength)
            {
                ShowInputError($"Максимум {_inputMaxLength} символов!");
                return;
            }

            // Custom validator
            if (_inputValidator != null)
            {
                string error = _inputValidator(v);
                if (!string.IsNullOrEmpty(error))
                {
                    ShowInputError(error);
                    return;
                }
            }

            var action = _inputSubmitAction;
            CloseDialog();
            action?.Invoke(v);
        }

        private void InputCancel_Click(object sender, RoutedEventArgs e) => CloseDialog();

        private void TxtInput_Changed(object sender, TextChangedEventArgs e)
        {
            if (lblInputWatermark != null)
                lblInputWatermark.Visibility = string.IsNullOrEmpty(txtInputField.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowInputError(string msg)
        {
            lblInputHint.Text = msg;
            lblInputHint.Foreground = (Brush)FindResource("RedBrush");
            lblInputHint.Visibility = Visibility.Visible;
        }

        // Find the message border inside WinInfo (the one with CornerRadius=8, dark bg)
        private Border FindInfoMsgBorder()
        {
            // It's the border wrapping lblInfoMsg
            if (lblInfoMsg?.Parent is Border b) return b;
            return null;
        }
    }
}
