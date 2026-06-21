using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TunarrDummyStart
{
    public sealed class ChannelsConfigForm : Form
    {
        private readonly DataGridView dgvChannels;
        private readonly Button btnOK;
        private readonly Button btnCancel;
        private readonly List<ChannelConfig> _channelsCopy;

        public List<ChannelConfig> Channels => _channelsCopy;

        public ChannelsConfigForm(List<ChannelConfig> channels)
        {
            // Deep copy the channels list to avoid modifying the reference before user clicks OK
            _channelsCopy = new List<ChannelConfig>();
            foreach (var ch in channels)
            {
                _channelsCopy.Add(new ChannelConfig
                {
                    ChannelId = ch.ChannelId,
                    Url = ch.Url,
                    Enabled = ch.Enabled,
                    RetryCount = ch.RetryCount
                });
            }

            Text = "Configure Channels";
            Size = new Size(580, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(28, 28, 28);
            ForeColor = Color.FromArgb(220, 220, 220);

            // DataGridView Setup
            dgvChannels = new DataGridView
            {
                Location = new Point(12, 12),
                Size = new Size(540, 290),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.FromArgb(18, 18, 18),
                GridColor = Color.FromArgb(65, 65, 65),
                BorderStyle = BorderStyle.FixedSingle,
                EnableHeadersVisualStyles = false
            };

            // Custom styles
            var headerStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            dgvChannels.ColumnHeadersDefaultCellStyle = headerStyle;

            var cellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.FromArgb(220, 220, 220),
                SelectionBackColor = Color.FromArgb(0, 122, 204),
                SelectionForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            dgvChannels.DefaultCellStyle = cellStyle;

            // Columns
            var colId = new DataGridViewTextBoxColumn
            {
                Name = "colId",
                HeaderText = "Channel ID",
                Width = 90,
                ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle(cellStyle) { Alignment = DataGridViewContentAlignment.MiddleCenter }
            };
            
            var colEnabled = new DataGridViewCheckBoxColumn
            {
                Name = "colEnabled",
                HeaderText = "Enabled",
                Width = 70
            };

            var colUrl = new DataGridViewTextBoxColumn
            {
                Name = "colUrl",
                HeaderText = "Custom URL (Optional)",
                Width = 240
            };

            var colRetry = new DataGridViewTextBoxColumn
            {
                Name = "colRetry",
                HeaderText = "Retry Override",
                Width = 115,
                ToolTipText = "Leave empty to use global configuration"
            };

            dgvChannels.Columns.AddRange(colId, colEnabled, colUrl, colRetry);

            // Populate Rows
            foreach (var ch in _channelsCopy)
            {
                string retryVal = ch.RetryCount.HasValue ? ch.RetryCount.Value.ToString() : string.Empty;
                dgvChannels.Rows.Add(ch.ChannelId, ch.Enabled, ch.Url, retryVal);
            }

            // Buttons
            Color cBtn = Color.FromArgb(52, 52, 52);
            Color cBtnBorder = Color.FromArgb(80, 80, 80);

            btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(366, 335),
                Size = new Size(90, 30),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnOK.FlatAppearance.BorderColor = Color.FromArgb(0, 95, 165);
            btnOK.Click += BtnOK_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(462, 335),
                Size = new Size(90, 30),
                BackColor = cBtn,
                ForeColor = Color.FromArgb(220, 220, 220),
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.FlatAppearance.BorderColor = cBtnBorder;

            Controls.Add(dgvChannels);
            Controls.Add(btnOK);
            Controls.Add(btnCancel);

            AcceptButton = btnOK;
            CancelButton = btnCancel;
        }

        private void BtnOK_Click(object? sender, EventArgs e)
        {
            // Parse grid entries back to our local copy list
            for (int i = 0; i < dgvChannels.Rows.Count; i++)
            {
                var row = dgvChannels.Rows[i];
                var enabled = Convert.ToBoolean(row.Cells["colEnabled"].Value);
                var url = Convert.ToString(row.Cells["colUrl"].Value)?.Trim() ?? string.Empty;
                var retryStr = Convert.ToString(row.Cells["colRetry"].Value)?.Trim();
                
                int? retryCount = null;
                if (!string.IsNullOrEmpty(retryStr) && int.TryParse(retryStr, out int parsedRetry))
                {
                    retryCount = parsedRetry >= 0 ? parsedRetry : 0;
                }

                _channelsCopy[i].Enabled = enabled;
                _channelsCopy[i].Url = url;
                _channelsCopy[i].RetryCount = retryCount;
            }
        }
    }
}
