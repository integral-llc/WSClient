using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Newtonsoft.Json;
using WebSocketSharp;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace WSClient
{
    public partial class MainForm : Form
    {
        private WebSocket _ws;
        private bool _connected;
        private string _settingsFile;

        public class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public Color ForeColor { get; set; }
            public string Message { get; set; }
            public bool? IsIn { get; set; }
        }

        public class Settings
        {
            public string LastServer { get; set; }
            public List<string> CommandsHistory { get; set; } 
        }

        private readonly List<LogEntry> _logEntries = new List<LogEntry>(); 
        private Settings _settings;
        private int _historyIndex;

        public MainForm()
        {
            InitializeComponent();
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (_ws != null && _ws.IsAlive)
                _ws.Close();

            if (_ws != null)
            {
                _ws.OnOpen -= WsOnOpen;
                _ws.OnMessage -= WsOnMessage;
                _ws.OnClose -= WsOnClose;
                _ws.OnError -= WsOnError;
            }

            _connected = false;

            _ws = new WebSocket(txtUrl.Text);
            _ws.OnOpen += WsOnOpen;
            _ws.OnMessage += WsOnMessage;
            _ws.OnClose += WsOnClose;
            _ws.OnError += WsOnError;
            _ws.ConnectAsync();

            btnConnect.Enabled = false;
            txtIn.Enabled = false;
            AddLog(Color.Black, "Connecting...");

            _settings.LastServer = txtUrl.Text;
        }

        private void WsOnError(object sender, ErrorEventArgs args)
        {
            if (args.Exception != null)
                AddLog(Color.Red, args.Message + "\r\n" + args.Exception);
            else
                AddLog(Color.Red, args.Message);
        }

        private void WsOnClose(object sender, CloseEventArgs closeEventArgs)
        {
            AddLog(Color.Black, "Disconnected");
            Action a = () =>
                {
                    btnConnect.Enabled = true;
                    btnConnect.Text = "Connect";
                };

            _connected = false;
            BeginInvoke(a);
        }

        private void WsOnMessage(object sender, MessageEventArgs args)
        {
            AddLog(Color.Black, args.Data, true);
        }

        private void WsOnOpen(object sender, EventArgs eventArgs)
        {
            _connected = true;
            AddLog(Color.Green, "Connected");
            Action a = () =>
                {
                    btnConnect.Text = "Disconnect";
                    btnConnect.Enabled = true;
                    txtIn.Enabled = true;
                };

            BeginInvoke(a);
        }

        private void AddLog(Color color, string message, bool? isIn = null)
        {
            Action a = () =>
                {
                    LogEntry entry = new LogEntry
                        {
                            ForeColor = color,
                            Message = message,
                            Timestamp = DateTime.Now,
                            IsIn = isIn
                        };

                    _logEntries.Add(entry);
                    lvLog.VirtualListSize = _logEntries.Count;
                };
            if (InvokeRequired)
                BeginInvoke(a);
            else
                a();
        }

        private void AddLog(Color color, string format, params object[] args)
        {
            AddLog(color, string.Format(format, args));    
        }

        private void lvLog_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            var entry = _logEntries[e.ItemIndex];
            ListViewItem item = new ListViewItem();
            item.Text = entry.Timestamp.ToString();
            string prefix = "";
            if (entry.IsIn.HasValue)
                prefix = entry.IsIn == true ? "> " : "< ";
            item.SubItems.Add(prefix + entry.Message.Substring(0, Math.Min(100, entry.Message.Length)));
            item.ForeColor = entry.ForeColor;
            e.Item = item;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            _settingsFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "settings.json");
            try
            {
                if (File.Exists(_settingsFile))
                {
                    _settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(_settingsFile));
                }
            }
            catch 
            {}

            if (_settings == null)
            {
                _settings = new Settings
                    {
                        CommandsHistory = new List<string>()
                    };
            }

            txtUrl.Text = _settings.LastServer;
            if (_settings.CommandsHistory.Count > 0)
                txtIn.Text = _settings.CommandsHistory[0];
        }

        private void lvLog_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvLog.SelectedIndices.Count == 0)    
                return;

            txtOut.Text = _logEntries[lvLog.SelectedIndices[0]].Message;
        }

        private void txtIn_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_connected)
                return;

            if (e.Control)
            {
                if (e.KeyCode == Keys.S && txtIn.Text.Length > 0)
                {
                    _ws.SendAsync(txtIn.Text, SendCompleted);
                    AddLog(Color.Black, txtIn.Text, false);
                    txtIn.Enabled = false;
                    while (_settings.CommandsHistory.Count > 100)
                        _settings.CommandsHistory.RemoveAt(_settings.CommandsHistory.Count - 1);
                    _settings.CommandsHistory.Insert(0, txtIn.Text);
                    _historyIndex = 0;
                }
                else if (e.KeyCode == Keys.Up)
                {
                    if (_historyIndex + 1 < _settings.CommandsHistory.Count)
                    {
                        _historyIndex++;
                        txtIn.Text = _settings.CommandsHistory[_historyIndex];
                    }
                }
                else if (e.KeyCode == Keys.Down)
                {
                    if (_historyIndex - 1 >= 0)
                    {
                        _historyIndex--;
                        txtIn.Text = _settings.CommandsHistory[_historyIndex];
                    }
                }
            }
        }

        private void SendCompleted(bool b)
        {
            Action a = () =>
                {
                    txtIn.Enabled = true;
                };

            BeginInvoke(a);
        }

        private void lvLog_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (lvLog.SelectedIndices.Count == 0)
                return;

            var entry = _logEntries[lvLog.SelectedIndices[0]];
            if (entry.IsIn == false)
                txtIn.Text = entry.Message;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            File.WriteAllText(_settingsFile, JsonConvert.SerializeObject(_settings));
        }

        private void txtOut_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.J)
            {
                if (txtOut.Text.Length > 1)
                {
                    try
                    {
                        dynamic parsedJson = JsonConvert.DeserializeObject(txtOut.Text);
                        txtOut.Text = JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
                    }
                    catch { }
                }
            }
        }
    }
}
