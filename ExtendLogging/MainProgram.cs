using Douyu.ID;
using DouyuDM_PluginFramework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace ExtendLogging
{
    public class MainProgram : DMPlugin
    {
        public static string ConfigPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), @"斗鱼弹幕姬\Plugins\ExtendLogging");

        private static VersionChecker VChecker { get; } = new VersionChecker("ExtendLogging");

        private PluginSettings PSettings { get; }
        
        private SettingsWindow SettingsWnd { get; }

        private DouyuID douyuID = new DouyuID();

        #region DmjReflections
        private Queue<MessageModel> MessageQueue { get; } = new Queue<MessageModel>();

        private object Statistics { get; }

        private PropertyInfo DanmakuCountShow { get; }

        private FieldInfo EnabledShowError { get; }

        private MethodInfo AddUser { get; }

        private MethodInfo Logging { get; }

        private MethodInfo AddDMText { get; }

        private MethodInfo BaseProcessMessage { get; }

        public Window DmjWnd { get; }
        #endregion

        public MainProgram()
        {
            if (!Directory.Exists(ConfigPath))
            {
                Directory.CreateDirectory(ConfigPath);
            }
            string filePath = Path.Combine(ConfigPath, "Config.cfg");
            PSettings = new PluginSettings(filePath);
            try
            {
                PSettings.LoadConfig();
            }
            catch (Exception Ex)
            {
                new FileInfo(filePath).MoveTo(Path.Combine(ConfigPath, "BrokenConfig.cfg"));
                PSettings.SaveConfig();
                MessageBox.Show($"载入配置文件失败:{Ex.ToString()}\n损坏的配置文件已保存至BrokenConfig.cfg,新的配置文件已生成", "更多日志", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (PSettings.Enabled)
            {
                this.Start();
            }
            SettingsWnd = new SettingsWindow(PSettings);
            DmjWnd = Application.Current.MainWindow;
            this.PluginName = "更多日志";
            this.PluginAuth = "Coel Wu & Executor丶";
            this.PluginVer = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            this.PluginDesc = "管理弹幕姬日志输出行为";
            this.PluginCont = "coelwu78@protonmail.com";
            Type dmjType = DmjWnd.GetType();
            MessageQueue = (Queue<MessageModel>)dmjType.GetField("messageQueue", BindingFlags.GetField | BindingFlags.Instance | BindingFlags.NonPublic).GetValue(DmjWnd);
            Logging = dmjType.GetMethod("Logging", BindingFlags.Instance | BindingFlags.Public);
            AddDMText = dmjType.GetMethod("AddDMText", BindingFlags.Instance | BindingFlags.Public);
            BaseProcessMessage = dmjType.GetMethod("ProcessMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            Statistics = dmjType.GetField("Statistics", BindingFlags.GetField | BindingFlags.Instance | BindingFlags.NonPublic).GetValue(DmjWnd);
            AddUser = Statistics.GetType().GetMethod("AddUser", BindingFlags.Instance | BindingFlags.Public);
            DanmakuCountShow = Statistics.GetType().GetProperty("DanmakuCountShow", BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public);
            EnabledShowError = dmjType.GetField("showErrorEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            ((Thread)dmjType.GetField("ProcessMessageThread", BindingFlags.GetField | BindingFlags.Instance | BindingFlags.NonPublic).GetValue(DmjWnd)).Abort();
            new Thread(() =>
            {
                while (true)
                {
                    lock (MessageQueue)
                    {
                        var count = 0;
                        if (MessageQueue.Any())
                        {
                            count = (int)Math.Ceiling(MessageQueue.Count / 30.0);
                        }

                        for (var i = 0; i < count; i++)
                        {
                            if (MessageQueue.Any())
                            {
                                var message = MessageQueue.Dequeue();
                                ProcessMessage(message);
                                if (message.MsgType == MsgTypeEnum.Comment)
                                {
                                    lock (Statistics)
                                    {
                                        AddDanmakuCountShow();
                                        AddUser.Invoke(Statistics, new object[] { message.UserName });
                                    }
                                }
                            }
                        }
                    }
                    Thread.Sleep(30);
                }
            }){ IsBackground = true }.Start();

        }

        public void AddDanmakuCountShow()
        {
            long x = (long)DanmakuCountShow.GetValue(Statistics);
            DanmakuCountShow.SetValue(Statistics, ++x);
        }

        private void ProcessMessage(MessageModel messageModel)
        {
            if (this.Status)
            {
                if (messageModel.MsgType == MsgTypeEnum.Comment)
                {
                    if (PSettings.LogLevel || PSettings.LogMedal || PSettings.LogTitle)
                    {
                        int UserLevel = messageModel.UserLevel;
                        if (!PSettings.EnableShieldLevel || UserLevel >= PSettings.ShieldLevel)
                        {
                            int UserMedalLevel = messageModel.UserBadgetLevel;
                            string UserMedalName = messageModel.UserBadget;
                            string UserNobility = douyuID.ParseNobility(messageModel.UserNobility);
                            string UserRoomIdentity = douyuID.ParseRoomIdentify(messageModel.UserRoomIdentity);
                            string prefix = $"{(UserRoomIdentity == "" ? "" : "[" + UserRoomIdentity + "]")}{(UserNobility == "" ? "" : "[" + UserNobility + "]")}{(PSettings.LogMedal && !string.IsNullOrEmpty(UserMedalName) ? $"{{{UserMedalName},{UserMedalLevel}}}" : null)}{(PSettings.LogLevel ? $"(UL {UserLevel})" : "")}{messageModel.UserName}";
                            Logging.Invoke(DmjWnd, new object[] { $"收到弹幕:{prefix} 说: {messageModel.CommentText}" });
                            AddDMText.Invoke(DmjWnd, new object[] { prefix, messageModel.CommentText, false, false, null, false });
                        }
                    }
                }
                else if (PSettings.LogExternInfo && (messageModel.MsgType == MsgTypeEnum.NotImplemented || messageModel.MsgType == MsgTypeEnum.LiveStatusToggle || messageModel.MsgType == MsgTypeEnum.UserBan || messageModel.MsgType == MsgTypeEnum.StreamerUpgrade || messageModel.MsgType == MsgTypeEnum.UserEnter || messageModel.MsgType == MsgTypeEnum.BadgetUpgrade || messageModel.MsgType == MsgTypeEnum.LiveShare))
                {
                    string type = messageModel.RawData.Get("type");
                    switch (type)
                    {
                        case "rss":
                            {
                                int status = messageModel.RawData.GetInt("ss");
                                if (status == 0)
                                {
                                    string toLog = "主播已下播";
                                    TwoAction(toLog);
                                }
                                else if (status == 1)
                                {
                                    string toLog = "主播已开播";
                                    TwoAction(toLog);
                                }
                                break;
                            }
                        case "upgrade":
                            {
                                string toLog = $"用户 {messageModel.UserName} 已升级到 {messageModel.UserLevel} 级";
                                TwoAction(toLog);
                                break;
                            }
                        case "upbc":
                            {
                                string toLog = $"主播 已升级到 {messageModel.UserLevel} 级";
                                TwoAction(toLog);
                                break;
                            }
                        case "newblackres":
                            {
                                string toLog = $"用户 {messageModel.TargetUserName} 已被 {messageModel.UserName} 禁言到 {messageModel.BanEndTime}";
                                TwoAction(toLog);
                                break;
                            }
                        case "blab":
                            {
                                string toLog = $"用户 {messageModel.UserName} 将勋章 {messageModel.UserBadget} 升级到了 {messageModel.UserBadgetLevel}";
                                TwoAction(toLog);
                                break;
                            }
                        case "srres":
                            {
                                string toLog = $"用户 {messageModel.UserName} 分享了直播间";
                                TwoAction(toLog);
                                break;
                            }
                        case "al":
                            {
                                string toLog = "主播暂时离开";
                                TwoAction(toLog);
                                break;
                            }
                        case "ab":
                            {
                                string toLog = "主播回来了";
                                TwoAction(toLog);
                                break;
                            }
                        default:
                            {
                                BaseProcessMessage.Invoke(DmjWnd, new object[] { messageModel });
                                break;
                            }
                    }
                }
                else if (messageModel.MsgType == MsgTypeEnum.GiftSend && PSettings.HideGifts)
                {
                    //Ignore
                }
                else
                {
                    BaseProcessMessage.Invoke(DmjWnd, new object[] { messageModel });
                }
            }
            else
            {
                BaseProcessMessage.Invoke(DmjWnd, new object[] { messageModel });
            }
        }

        private void TwoAction(string toLog)
        {
            Logging.Invoke(DmjWnd, new object[] { $"系统通知:{toLog}" });
            if ((bool)EnabledShowError.GetValue(DmjWnd))
            {
                DmjWnd.Dispatcher.Invoke(() => AddDMText.Invoke(DmjWnd, new object[] { "系统通知", toLog, true, false, null, false }));
            }
            else
            {
                DmjWnd.Dispatcher.Invoke(() => AddDMText.Invoke(DmjWnd, new object[] { "系统通知", toLog, false, false, null, false }));
            }
        }

        public override void Inited()
        {
            if (!VChecker.FetchInfo())
            {
                Log("版本检查失败 : " + VChecker.LastException.Message);
                return;
            }
            if (VChecker.HasNewVersion(this.PluginVer))
            {
                Log("有新版本啦~最新版本 : " + VChecker.Version + "\n                " + VChecker.UpdateDescription);
                Log("下载地址 : " + VChecker.DownloadUrl);
                Log("插件页面 : " + VChecker.WebPageUrl);
            }
        }

        public override void DeInit()
        {
            SettingsWnd.Closing -= SettingsWnd.Window_Closing;
            SettingsWnd.Close();
        }

        public override void Admin()
        {
            SettingsWnd.Show();
            SettingsWnd.Topmost = true;
            SettingsWnd.Topmost = false;
        }

        public override void Start()
        {
            PSettings.Enabled = true;
            base.Start();
        }

        public override void Stop()
        {
            PSettings.Enabled = false;
            base.Stop();
        }
    }
}
