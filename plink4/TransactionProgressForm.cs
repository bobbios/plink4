using System;
using System.Drawing;
using System.IO;
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

        private static readonly string PowerCycleImagePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "PowerCycleSteps.png");

        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly PictureBox _powerCycleImage;
        private readonly Button _actionButton;
        private readonly Timer _timeoutTimer;
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
            ClientSize = new Size(620, 380);

            _timeoutTimer = new Timer { Interval = Math.Max(1, timeoutMs) };
            _timeoutTimer.Tick += OnTimeoutTick;
            _timeoutTimer.Start();

            FormClosed += (s, e) =>
            {
                _timeoutTimer.Stop();
                _timeoutTimer.Dispose();
            };

            _statusLabel = new Label
            {
                Text = message,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 20),
                Size = new Size(580, 180),
                Font = new Font(Font.FontFamily, 20F, FontStyle.Bold)
            };

            // The SDK gives no real progress signal — DoCredit/DoDebit/DoEbt is a
            // single blocking call with no intermediate status callback — so this
            // is deliberately an indeterminate marquee, not a real percentage.
            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Location = new Point(20, 210),
                Size = new Size(580, 36)
            };

            // Disabled until the caller confirms the terminal is actually connected
            // (see SetTerminalConnected) — before that there's no live session for
            // Cancel to do anything to.
            _actionButton = new Button
            {
                Text = "Cancel",
                Size = new Size(200, 60),
                Location = new Point(210, 290),
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                Enabled = false
            };
            _actionButton.Click += OnActionButtonClick;

            _powerCycleImage = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Visible = false
            };

            Controls.Add(_statusLabel);
            Controls.Add(_progressBar);
            Controls.Add(_powerCycleImage);
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

        public void SetTerminalConnected()
        {
            RunOnUiThread(() =>
            {
                if (_mode == Mode.Working && !CancelRequested && !TimedOut)
                    _actionButton.Enabled = true;
            });
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

                // Grow the dialog to make room for the power-cycle steps below the message —
                // the compact "working" size doesn't have space for a full illustrated strip.
                ClientSize = new Size(940, 720);

                _statusLabel.Location = new Point(20, 15);
                _statusLabel.Size = new Size(900, 180);
                _statusLabel.ForeColor = Color.Firebrick;
                _statusLabel.Font = new Font(Font.FontFamily, 13F, FontStyle.Regular);
                _statusLabel.Text = message + "\n\nIf this keeps happening, power-cycle the terminal:";

                _progressBar.Visible = false;

                TryShowPowerCycleImage();

                _actionButton.Location = new Point((ClientSize.Width - _actionButton.Width) / 2, 640);
                _actionButton.Text = "Close";
                _actionButton.Enabled = true;

                CenterToScreen();
            });
        }

        private void TryShowPowerCycleImage()
        {
            try
            {
                if (_powerCycleImage.Image == null && File.Exists(PowerCycleImagePath))
                    _powerCycleImage.Image = Image.FromFile(PowerCycleImagePath);
            }
            catch
            {
                // A missing/corrupt image shouldn't block showing the error itself.
            }

            if (_powerCycleImage.Image == null) return;

            _powerCycleImage.Location = new Point(20, 205);
            _powerCycleImage.Size = new Size(900, 420);
            _powerCycleImage.Visible = true;
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
