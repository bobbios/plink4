using System;
using System.Drawing;
using System.Windows.Forms;

namespace plink4
{
    internal sealed class TransactionProgressForm : Form
    {
        private enum Mode
        {
            Working,
            Error
        }

        // Operators tend to reflex-click Cancel the instant the dialog appears,
        // before the terminal's even had a chance to respond — hold the button
        // off for a few seconds so an early click doesn't cancel a request that
        // was never really sent yet.
        private const int CancelEnableDelayMs = 3000;

        private readonly Label _statusLabel;
        private readonly Button _actionButton;
        private readonly Timer _timeoutTimer;
        private readonly Timer _cancelEnableTimer;
        private Mode _mode = Mode.Working;

        public bool CancelRequested { get; private set; }
        public bool ErrorAcknowledged { get; private set; }
        public bool TimedOut { get; private set; }

        public event EventHandler CancelClicked;

        public TransactionProgressForm(string message, int timeoutMs)
        {
            Text = "Processing Transaction";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            ClientSize = new Size(380, 170);

            _timeoutTimer = new Timer { Interval = Math.Max(1, timeoutMs) };
            _timeoutTimer.Tick += OnTimeoutTick;
            _timeoutTimer.Start();

            _cancelEnableTimer = new Timer { Interval = CancelEnableDelayMs };
            _cancelEnableTimer.Tick += OnCancelEnableTick;
            _cancelEnableTimer.Start();

            FormClosed += (s, e) =>
            {
                _timeoutTimer.Stop();
                _timeoutTimer.Dispose();
                _cancelEnableTimer.Stop();
                _cancelEnableTimer.Dispose();
            };

            _statusLabel = new Label
            {
                Text = message,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 15),
                Size = new Size(360, 90),
                Font = new Font(Font.FontFamily, 10F)
            };

            _actionButton = new Button
            {
                Text = "Cancel",
                Size = new Size(100, 32),
                Location = new Point(140, 115),
                Enabled = false
            };
            _actionButton.Click += OnActionButtonClick;

            Controls.Add(_statusLabel);
            Controls.Add(_actionButton);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Force the window above whatever else has focus — a plain TopMost
            // assignment at construction time doesn't always win the z-order fight.
            TopMost = false;
            TopMost = true;
            Activate();
            BringToFront();
        }

        private void OnCancelEnableTick(object sender, EventArgs e)
        {
            _cancelEnableTimer.Stop();
            if (_mode == Mode.Working && !CancelRequested && !TimedOut)
                _actionButton.Enabled = true;
        }

        private void OnTimeoutTick(object sender, EventArgs e)
        {
            _timeoutTimer.Stop();

            if (CancelRequested || TimedOut) return;

            TimedOut = true;
            DialogResult = DialogResult.Ignore;
            Close();
        }

        private void OnActionButtonClick(object sender, EventArgs e)
        {
            _timeoutTimer.Stop();

            if (_mode == Mode.Error)
            {
                ErrorAcknowledged = true;
                DialogResult = DialogResult.Abort;
                Close();
                return;
            }

            if (CancelRequested) return;

            CancelRequested = true;
            _actionButton.Enabled = false;
            _statusLabel.Text = "Cancelling...";

            CancelClicked?.Invoke(this, EventArgs.Empty);

            DialogResult = DialogResult.Cancel;
            Close();
        }

        public void UpdateStatus(string message)
        {
            RunOnUiThread(() =>
            {
                if (_mode != Mode.Working) return;
                _statusLabel.Text = message;
            });
        }

        public void ShowError(string message)
        {
            RunOnUiThread(() =>
            {
                if (CancelRequested || TimedOut) return;

                _mode = Mode.Error;
                Text = "Terminal Connection Error";
                _statusLabel.ForeColor = Color.Firebrick;
                _statusLabel.Text = message;
                _actionButton.Text = "Close";
                _actionButton.Enabled = true;
            });
        }

        public void CompleteAndClose()
        {
            RunOnUiThread(() =>
            {
                if (_mode == Mode.Working && !CancelRequested && !TimedOut)
                {
                    _timeoutTimer.Stop();
                    DialogResult = DialogResult.OK;
                    Close();
                }
            });
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                try { BeginInvoke(action); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            action();
        }
    }
}
