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
        private readonly Label _cancelHintLabel;
        private readonly Label _elapsedLabel;
        private readonly Label _versionLabel;
        private readonly Button _actionButton;
        private readonly Timer _timeoutTimer;
        private readonly Timer _elapsedTimer;
        private readonly System.Diagnostics.Stopwatch _elapsedStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private Mode _mode = Mode.Working;

        // Static file version + build timestamp shown on the dialog so operators/testers
        // can tell which build is actually running without checking the file system.
        private static string GetVersionInfoText()
        {
            try
            {
                string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                var built = File.GetLastWriteTime(path);
                return $"plink4 v{ver.FileVersion} — built {built:yyyy-MM-dd HH:mm}";
            }
            catch
            {
                return "plink4";
            }
        }

        public bool ErrorAcknowledged { get; private set; }
        public bool TimedOut { get; private set; }

        public TransactionProgressForm(string message, int timeoutMs, bool allowCancel = true)
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

            _elapsedTimer = new Timer { Interval = 1000 };
            _elapsedTimer.Tick += OnElapsedTick;
            _elapsedTimer.Start();

            FormClosed += (s, e) =>
            {
                _timeoutTimer.Stop();
                _timeoutTimer.Dispose();
                _elapsedTimer.Stop();
                _elapsedTimer.Dispose();
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

            // The terminal itself is cancelled via its physical red X key, not from
            // this app — this button now only appears for the Error/Close state.
            _actionButton = new Button
            {
                Size = new Size(200, 60),
                Location = new Point(210, 290),
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                Enabled = false,
                Visible = false
            };
            _actionButton.Click += OnActionButtonClick;

            _cancelHintLabel = new Label
            {
                Text = "To cancel, press the red X button on the terminal.",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 300),
                Size = new Size(580, 40),
                Font = new Font(Font.FontFamily, 11F, FontStyle.Italic),
                ForeColor = Color.DimGray,
                Visible = allowCancel
            };

            _elapsedLabel = new Label
            {
                Text = "Elapsed: 0s",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 250),
                Size = new Size(580, 20),
                Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
                ForeColor = Color.Gray
            };

            _powerCycleImage = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Visible = false
            };

            _versionLabel = new Label
            {
                Text = GetVersionInfoText(),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(20, 358),
                Size = new Size(580, 16)
            };

            Controls.Add(_statusLabel);
            Controls.Add(_progressBar);
            Controls.Add(_powerCycleImage);
            Controls.Add(_cancelHintLabel);
            Controls.Add(_elapsedLabel);
            Controls.Add(_versionLabel);
            Controls.Add(_actionButton);
        }

        private void OnElapsedTick(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            _elapsedLabel.Text = $"Elapsed: {_elapsedStopwatch.Elapsed.Minutes}m {_elapsedStopwatch.Elapsed.Seconds}s";
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

            if (TimedOut) return;

            TimedOut = true;
            DialogResult = DialogResult.Ignore;
            Close();
        }

        private void OnActionButtonClick(object sender, EventArgs e)
        {
            _timeoutTimer.Stop();

            if (_mode != Mode.Error) return;

            ErrorAcknowledged = true;
            DialogResult = DialogResult.Abort;
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
                if (TimedOut) return;

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
                _cancelHintLabel.Visible = false;

                TryShowPowerCycleImage();

                _actionButton.Location = new Point((ClientSize.Width - _actionButton.Width) / 2, 640);
                _actionButton.Text = "Close";
                _actionButton.Visible = true;
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
                if (_mode == Mode.Working && !TimedOut)
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
