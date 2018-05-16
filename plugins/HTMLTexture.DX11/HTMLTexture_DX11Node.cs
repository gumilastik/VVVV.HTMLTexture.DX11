#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;

using VVVV.Core.Logging;

using Chromium;
using Chromium.Remote;
using Chromium.Event;
using Chromium.Remote.Event;

using FeralTic.DX11;
using FeralTic.DX11.Resources;

using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Linq;
using System.IO;

using System.Linq;
using System.Reflection;

using VVVV.Utils.IO;
using VVVV.Utils.Win32;
using System.Globalization;

#endregion usings

/*
TODO:
 - use CfxDomVisitor instead of HtmlAgilityPack
 - rebuild CEF with http://www.magpcss.org/ceforum/viewtopic.php?f=6&t=12794
 - touch events support
   recompiling with patches https://bitbucket.org/chromiumembedded/cef/issues/1059 
   test with http://rawgit.com/hammerjs/touchemulator/master/tests/manual/hammer.html
   e.CommandLine.AppendSwitchWithValue("touch-events", "enabled");
 - PepperFlash from Canary
 */

namespace VVVV.DX11.Nodes
{
    class App
    {
        public static int Main(string[] args)
        {
            return CfxRuntime.ExecuteProcess(null);
        }
    }

    internal class RenderProcess
    {
        static List<RenderProcess> renderProcess = new List<RenderProcess>(); // prevent from compiler's optimization

        internal static int RenderProcessMain()
        {
            RenderProcess rp = new RenderProcess();

            renderProcess.Add(rp);
            int retval = rp.Start();
            renderProcess.Remove(rp);

            return retval;
        }

        private CfrApp app;
        private CfrLoadHandler loadHandler;
        private CfrRenderProcessHandler renderProcessHandler;
        private RenderProcess()
        {

        }

        private int Start()
        {
            try
            {
                app = new CfrApp();

                loadHandler = new CfrLoadHandler();
                loadHandler.OnLoadEnd += loadHandler_OnLoadEnd;
                loadHandler.OnLoadStart += loadHandler_OnLoadStart;

                renderProcessHandler = new CfrRenderProcessHandler();
                renderProcessHandler.GetLoadHandler += (sender, e) => e.SetReturnValue(loadHandler);

                app.GetRenderProcessHandler += (s, e) => e.SetReturnValue(renderProcessHandler);

                var retval = CfrRuntime.ExecuteProcess(app);
                return retval;
            }
            catch
            {
                return 0;
            }
        }

        void loadHandler_OnLoadStart(object sender, CfrOnLoadStartEventArgs e)
        {
            if (e.Frame.IsMain)
            {
                if (HTMLTextureNode.nodes.ContainsKey(e.Browser.Identifier))
                {
                    HTMLTextureNode.nodes[e.Browser.Identifier].SetRemoteBrowser(e.Browser);
                    HTMLTextureNode.nodes[e.Browser.Identifier].OnLoadStart();
                }
            }
        }

        void loadHandler_OnLoadEnd(object sender, CfrOnLoadEndEventArgs e)
        {
            if (e.Frame.IsMain)
            {
                if (HTMLTextureNode.nodes.ContainsKey(e.Browser.Identifier))
                {
                    //  HTMLTextureNode.nodes[e.Browser.Identifier].SetRemoteBrowser(e.Browser);
                    HTMLTextureNode.nodes[e.Browser.Identifier].OnLoadEnd();
                }
            }
        }
    }

    [Startable]
    public class HTMLTextureStartable : IStartable
    {
        void app_OnBeforeCommandLineProcessing(object sender, Chromium.Event.CfxOnBeforeCommandLineProcessingEventArgs e)
        {
            // speech api
            e.CommandLine.AppendSwitch("enable-speech-input");

            // fix
            e.CommandLine.AppendSwitch("disable-gpu-compositing");

            // enable pepper flash or system Flash
            if (Directory.Exists(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"cef\PepperFlash")))
            {
                e.CommandLine.AppendSwitchWithValue("ppapi-flash-version", "19.0.0.201");
                e.CommandLine.AppendSwitchWithValue("ppapi-flash-path", Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"cef\PepperFlash\pepflashplayer.dll"));
            }
            else
            {
                e.CommandLine.AppendSwitch("enable-system-flash");
            }

            // MessageBox.Show(e.CommandLine.CommandLineString);
        }

        // Main entry point when called by vvvv
        void IStartable.Start()
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            CfxRuntime.LibCefDirPath = assemblyDir;

            CfxApp app = new CfxApp();
            app.OnBeforeCommandLineProcessing += app_OnBeforeCommandLineProcessing;

            var settings = new CfxSettings
           {
               //PackLoadingDisabled = true,
               WindowlessRenderingEnabled = true,
               NoSandbox = true,
               UserDataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "userdata"),
               CachePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "cache"),
               BrowserSubprocessPath = Assembly.GetExecutingAssembly().Location,
               LogSeverity = CfxLogSeverity.Disable,
               SingleProcess = false, // DEBUG
               MultiThreadedMessageLoop = true, // false
               IgnoreCertificateErrors = true,
           };

            CfxRuntime.Initialize(settings, app, RenderProcess.RenderProcessMain);
        }
        void IStartable.Shutdown()
        {
            CfxRuntime.Shutdown();
        }
    }

    #region PluginInfo
    [PluginInfo(Name = "HTMLTexture", AutoEvaluate = true, Category = "DX11.Texture", Version = "URL", Help = "", Tags = "browser, ChromiumFX")]
    #endregion PluginInfo
    public class URLDX11_TextureHTMLTextureNode : HTMLTextureNode, IPluginEvaluate
    {
        [Input("Url", DefaultString = DEFAULT_URL)]
        public ISpread<string> FUrlIn;

        public const string DEFAULT_URL = "http://vvvv.org";

        public override void Start()
        {
            LoadUrl(FUrlIn[0]);
        }

        public void Evaluate(int SpreadMax)
        {
            if (FUrlIn.IsChanged)
            {
                LoadUrl(FUrlIn[0]);
            }
            Update(SpreadMax);
        }
    }

    #region PluginInfo
    [PluginInfo(Name = "HTMLTexture", AutoEvaluate = true, Category = "DX11.Texture", Version = "String", Help = "", Tags = "browser, ChromiumFX")]
    #endregion PluginInfo
    public class StringDX11_TextureHTMLTextureNode : HTMLTextureNode, IPluginEvaluate
    {
        [Input("HTML", DefaultString = @"<html><head></head><body bgcolor=""#ffffff""></body></html>")]
        public ISpread<string> FHtmlIn;

        public override void Start()
        {
            LoadString(FHtmlIn[0]);
        }

        public void Evaluate(int SpreadMax)
        {
            if (FHtmlIn.IsChanged)
            {
                LoadString(FHtmlIn[0]);
            }
            Update(SpreadMax);
        }
    }

    public abstract class HTMLTextureNode : IDX11ResourceHost, IDisposable
    {
        public static Dictionary<int, HTMLTextureNode> nodes = new Dictionary<int, HTMLTextureNode>();

        public const int DEFAULT_WIDTH = 800;
        public const int DEFAULT_HEIGHT = 1000;

        public string LIVEPAGE_LOAD_FUNC;
        public string LIVEPAGE_UNLOAD_FUNC;

        #region fields & pins
        [Input("Reload", IsBang = true)]
        public ISpread<bool> FReloadIn;

        [Input("Width", DefaultValue = DEFAULT_WIDTH)]
        public IDiffSpread<int> FWidthIn;

        [Input("Height", DefaultValue = DEFAULT_HEIGHT)]
        public IDiffSpread<int> FHeightIn;

        [Input("Transparent")]
        public IDiffSpread<bool> FTransparentIn;

        [Input("Popup")]
        public IDiffSpread<bool> FPopupIn;

        [Input("Filter Url")]
        public ISpread<string> FFilterUrlIn;

        [Input("Zoom Level")]
        public IDiffSpread<double> FZoomLevelIn;

        [Input("Touch", Visibility = PinVisibility.OnlyInspector)]
        public IDiffSpread<Vector2D> FTouchIn;

        [Input("Mouse Event")]
        public ISpread<Mouse> FMouseIn;

        [Input("Key Event Type", Visibility = PinVisibility.OnlyInspector)]
        public IDiffSpread<KeyNotificationKind> FKeyEventTypeIn;

        [Input("Key Code", Visibility = PinVisibility.OnlyInspector)]
        public IDiffSpread<int> FKeyCodeIn;

        [Input("Key Char", Visibility = PinVisibility.OnlyInspector)]
        public IDiffSpread<string> FKeyCharIn;

        [Input("Key Event")]
        public ISpread<Keyboard> FKeyboardIn;

        [Input("Scroll To")]
        public IDiffSpread<Vector2D> FScrollToIn;

        [Input("Update DOM", IsBang = true)]
        public ISpread<bool> FUpdateDomIn;

        [Input("Object", DefaultString = "vvvv")]
        public ISpread<string> FObjectIn;

        [Input("Method", DefaultString = "hello")]
        public IDiffSpread<string> FMethodIn;

        [Input("Bind", IsBang = true)]
        public ISpread<bool> FBindIn;

        [Input("JavaScript")]
        public ISpread<string> FJavaScriptIn;

        [Input("Execute", IsBang = true)]
        public ISpread<bool> FExecuteIn;

        [Input("Evaluate JavaScript", IsBang = true)]
        public ISpread<bool> FEvaluateJavaScriptIn;

        [Input("Show DevTools", IsBang = true)]
        public IDiffSpread<bool> FShowDevToolsIn;

        [Input("LivePage")]
        public IDiffSpread<bool> FLivePageIn;

        [Input("User-Agent")]
        public ISpread<string> FUserAgentIn;

        [Input("Console")]
        public IDiffSpread<bool> FConsoleIn;

        [Input("Enabled", DefaultBoolean = true)]
        public ISpread<bool> FEnabledIn;


        [Output("Output", IsSingle = true)]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        [Output("Root Element")]
        public ISpread<XElement> FRootElementOut;

        [Output("Document")]
        public ISpread<XDocument> FDomOut;

        [Output("Method", IsBang = true)]
        public ISpread<bool> FMethodOut;

        [Output("Result")]
        public ISpread<string> FResultOut;

        [Output("Result JS")]
        public ISpread<string> FResultJSOut;

        [Output("Document Width")]
        public ISpread<int> FDocumentWidthOut;

        [Output("Document Height")]
        public ISpread<int> FDocumentHeightOut;

        [Output("Is Loading")]
        public ISpread<bool> FIsLoadingOut;

        [Output("Current Url")]
        public ISpread<string> FCurrentUrlOut;

        [Output("Error Text")]
        public ISpread<string> FErrorTextOut;


        [Import()]
        public ILogger FLogger;
        #endregion fields & pins

        private bool init;
        private bool invalidate;
        private bool createBrowser;
        private bool isImageReady;

        private bool isTouch;
        private CfxMouseEvent mouseEvent; // last coords

        private List<int> keyCode; // keyboard

        private int width;
        private int height;
        private double zoomLevel;
        private Vector2D scroll;
        private bool[] method;
        private bool isDocumentReady;

        protected CfxBrowser browser;
        private CfrBrowser remoteBrowser;
        private CfrV8Handler v8Handler;
        private CfxStringVisitor visitor;

        private CfxClient client;
        private CfxLifeSpanHandler lifeSpanHandler;
        private CfxLoadHandler loadHandler;
        private CfxRenderHandler renderHandler;
        private CfxRequestHandler requestHandler;
        private CfxDisplayHandler displayHandler;
        private CfxBrowserSettings settings;

        private byte[] image;

        private object bLock = new object();
        private object bLock2 = new object();

        DX11DynamicTexture2D texture;

        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern IntPtr memcpy(IntPtr dest, IntPtr src, UIntPtr count);

        #region mouse & keyboard
        private Subscription<Mouse, MouseNotification> FMouseSubscription;
        private int FMouseWheel;
        public Mouse Mouse
        {
            set
            {
                if (FMouseSubscription == null)
                {
                    FMouseSubscription = new Subscription<Mouse, MouseNotification>(
                    mouse => mouse.MouseNotifications,
                    (mouse, n) =>
                    {
                        if (FEnabledIn[0] && browser != null)
                        {
                            var mouseButtonNotification = n as MouseButtonNotification;

                            CfxMouseEvent mouseEvent = new CfxMouseEvent();

                            mouseEvent.X = (int)VMath.Map(n.Position.X, 0, n.ClientArea.Width, 0, width, TMapMode.Float);
                            mouseEvent.Y = (int)VMath.Map(n.Position.Y, 0, n.ClientArea.Height, 0, height, TMapMode.Float);

                            CfxMouseButtonType mouseButton = CfxMouseButtonType.Left;

                            if (n.Kind == MouseNotificationKind.MouseUp || n.Kind == MouseNotificationKind.MouseDown)
                            {
                                switch (mouseButtonNotification.Buttons)
                                {
                                    case MouseButtons.Left:
                                        mouseButton = CfxMouseButtonType.Left;
                                        break;
                                    case MouseButtons.Middle:
                                        mouseButton = CfxMouseButtonType.Middle;
                                        break;
                                    case MouseButtons.Right:
                                        mouseButton = CfxMouseButtonType.Right;
                                        break;
                                    default:
                                        mouseButton = CfxMouseButtonType.Left;
                                        break;
                                }
                            }

                            switch (n.Kind)
                            {
                                case MouseNotificationKind.MouseDown:
                                    {
                                        browser.Host.SendMouseClickEvent(mouseEvent, mouseButton, false, 1);
                                    }
                                    break;
                                case MouseNotificationKind.MouseUp:
                                    {
                                        browser.Host.SendMouseClickEvent(mouseEvent, mouseButton, true, 1);
                                    }
                                    break;
                                case MouseNotificationKind.MouseMove:
                                    {
                                        browser.Host.SendMouseMoveEvent(mouseEvent, false);
                                    }
                                    break;
                                case MouseNotificationKind.MouseWheel:
                                    {
                                        var mouseWheel = n as MouseWheelNotification;
                                        var wheel = FMouseWheel;
                                        FMouseWheel += mouseWheel.WheelDelta;
                                        int delta = (int)Math.Round((float)(FMouseWheel - wheel) / Const.WHEEL_DELTA);

                                        browser.Host.SendMouseWheelEvent(mouseEvent, 0, mouseWheel.WheelDelta);
                                    }
                                    break;
                                default:
                                    break;
                            }

                        }
                    }
                    );
                }
                FMouseSubscription.Update(value);
            }
        }

        private Subscription<Keyboard, KeyNotification> FKeyboardSubscription;
        private Keyboard FKeyboard;
        public Keyboard Keyboard
        {
            set
            {
                if (FKeyboardSubscription == null)
                    FKeyboardSubscription = new Subscription<Keyboard, KeyNotification>(
                    keyboard => keyboard.KeyNotifications,
                    (keyboard, n) =>
                    {
                        if (FEnabledIn[0] && browser != null)
                        {
                            CfxKeyEvent keyEvent = new CfxKeyEvent();
                            keyEvent.Modifiers = (uint)(FKeyboard.Modifiers) >> 15;

                            switch (n.Kind)
                            {
                                case KeyNotificationKind.KeyDown:
                                    {
                                        var keyDown = n as KeyDownNotification;

                                        keyEvent.Type = CfxKeyEventType.Keydown;
                                        keyEvent.WindowsKeyCode = (int)keyDown.KeyCode;
                                        keyEvent.NativeKeyCode = (int)keyDown.KeyCode;
                                    }
                                    break;
                                case KeyNotificationKind.KeyPress:
                                    {
                                        var keyPress = n as KeyPressNotification;

                                        keyEvent.Type = CfxKeyEventType.Char;
                                        keyEvent.Character = (short)keyPress.KeyChar;
                                        keyEvent.UnmodifiedCharacter = (short)keyPress.KeyChar;
                                        keyEvent.WindowsKeyCode = (int)keyPress.KeyChar;
                                        keyEvent.NativeKeyCode = (int)keyPress.KeyChar;
                                    }
                                    break;
                                case KeyNotificationKind.KeyUp:
                                    {
                                        var keyUp = n as KeyUpNotification;

                                        keyEvent.Type = CfxKeyEventType.Keyup;
                                        keyEvent.WindowsKeyCode = (int)keyUp.KeyCode;
                                        keyEvent.NativeKeyCode = (int)keyUp.KeyCode;
                                    }
                                    break;
                                default:
                                    break;
                            }

                            browser.Host.SendKeyEvent(keyEvent);
                        }
                    }
                    );
                FKeyboard = value;
                FKeyboardSubscription.Update(value);
            }
        }
        #endregion mouse & keyboard

        //called when data for any output pin is requested
        public void Update(int SpreadMax)
        {
            if (this.FTextureOutput[0] == null)
            {
                this.FTextureOutput[0] = new DX11Resource<DX11DynamicTexture2D>();
            }

            if (!init)
            {
                width = DEFAULT_WIDTH;
                height = DEFAULT_HEIGHT;

                lifeSpanHandler = new CfxLifeSpanHandler();
                lifeSpanHandler.OnAfterCreated += lifeSpanHandler_OnAfterCreated;
                lifeSpanHandler.OnBeforePopup += lifeSpanHandler_OnBeforePopup;

                renderHandler = new CfxRenderHandler();
                renderHandler.GetViewRect += renderHandler_GetViewRect;
                renderHandler.GetRootScreenRect += renderHandler_GetRootScreenRect;
                renderHandler.OnPaint += renderHandler_OnPaint;

                loadHandler = new CfxLoadHandler();
                loadHandler.OnLoadError += loadHandler_OnLoadError;
                loadHandler.OnLoadingStateChange += loadHandler_OnLoadingStateChange;

                requestHandler = new CfxRequestHandler();
                requestHandler.OnBeforeBrowse += requestHandler_OnBeforeBrowse;
                requestHandler.OnBeforeResourceLoad += requestHandler_OnBeforeResourceLoad;


                displayHandler = new CfxDisplayHandler();
                displayHandler.OnConsoleMessage += displayHandler_OnConsoleMessage;
                client = new CfxClient();
                client.GetLifeSpanHandler += (sender, e) => e.SetReturnValue(lifeSpanHandler);
                client.GetRenderHandler += (sender, e) => e.SetReturnValue(renderHandler);
                client.GetLoadHandler += (sender, e) => e.SetReturnValue(loadHandler);
                client.GetRequestHandler += (sender, e) => e.SetReturnValue(requestHandler); ;
                client.GetDisplayHandler += (sender, e) => e.SetReturnValue(displayHandler); ;



                settings = new CfxBrowserSettings();
                settings.WindowlessFrameRate = 60;
                settings.Webgl = CfxState.Enabled;
                settings.Plugins = CfxState.Enabled;
                settings.ApplicationCache = CfxState.Enabled;
                settings.CaretBrowsing = CfxState.Enabled;
                settings.Javascript = CfxState.Enabled;
                settings.FileAccessFromFileUrls = CfxState.Enabled;
                settings.UniversalAccessFromFileUrls = CfxState.Enabled;
                settings.WebSecUrity = CfxState.Disabled;

                visitor = new CfxStringVisitor();
                visitor.Visit += visitor_Visit;

                // set path to js
                string lpPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "livepage");


                LIVEPAGE_LOAD_FUNC =
                File.ReadAllText(Path.Combine(lpPath, "load.js")) +
                File.ReadAllText(Path.Combine(lpPath, "live_resource.js")) +
                File.ReadAllText(Path.Combine(lpPath, "livepage.js"));

                LIVEPAGE_UNLOAD_FUNC = File.ReadAllText(Path.Combine(lpPath, "unload.js"));

                mouseEvent = new CfxMouseEvent();
                keyCode = new List<int>();

                createBrowser = true;
                invalidate = true;
                init = true;
            }

            if (FTransparentIn.IsChanged)
            {
                createBrowser = true;
            }

            if (createBrowser)
            {
                var windowInfo = new CfxWindowInfo();
                windowInfo.SetAsWindowless(FTransparentIn[0]);
                //windowInfo.WindowlessRenderingEnabled = true;
                //windowInfo.TransparentPaintingEnabled = false;

                if (browser != null)
                {
                    browser.Host.CloseBrowser(true);
                    browser = null;
                }

                CfxBrowserHost.CreateBrowser(windowInfo, client, "", settings, null);

                createBrowser = false;
            }

            if (FMethodIn.IsChanged)
            {
                if (method == null || method.Length != FMethodIn.SliceCount) method = new bool[FMethodIn.SliceCount];
                FResultOut.SliceCount = FMethodIn.SliceCount;
                FMethodOut.SliceCount = FMethodIn.SliceCount;
            }

            if (FLivePageIn.IsChanged)
            {
                ExecuteJavascript(FLivePageIn[0] ? LIVEPAGE_LOAD_FUNC : LIVEPAGE_UNLOAD_FUNC);
            }

            if (FZoomLevelIn.IsChanged)
            {
                zoomLevel = VMath.Map(FZoomLevelIn[0], 0, 1, 0, 10, TMapMode.Clamp);
            }

            for (int i = 0; i < FMethodOut.SliceCount; i++)
            {
                FMethodOut[i] = method[i];
                method[i] = false;
            }

            if (browser != null)
            {
                if (FShowDevToolsIn.IsChanged && FShowDevToolsIn[0])
                {
                    CfxWindowInfo windowInfo = new CfxWindowInfo();

                    windowInfo.Style = WindowStyle.WS_OVERLAPPEDWINDOW | WindowStyle.WS_CLIPCHILDREN | WindowStyle.WS_CLIPSIBLINGS | WindowStyle.WS_VISIBLE;
                    windowInfo.ParentWindow = IntPtr.Zero;
                    windowInfo.WindowName = "DevTools";
                    windowInfo.X = 200;
                    windowInfo.Y = 200;
                    windowInfo.Width = 800;
                    windowInfo.Height = 600;

                    browser.Host.ShowDevTools(windowInfo, new CfxClient(), new CfxBrowserSettings(), null);
                }


                // mouse               
                if (FTouchIn.IsChanged && FTouchIn.SliceCount > 0)
                {
                    mouseEvent.X = (int)VMath.Map(FTouchIn[0].x, -1, 1, 0, width, TMapMode.Float);
                    mouseEvent.Y = (int)VMath.Map(FTouchIn[0].y, 1, -1, 0, height, TMapMode.Float);

                    if (!isTouch)
                    {
                        browser.Host.SendMouseClickEvent(mouseEvent, CfxMouseButtonType.Left, false, 1);
                    }
                    else
                    {
                        browser.Host.SendMouseMoveEvent(mouseEvent, false);

                    }
                    isTouch = true;
                }
                if (isTouch && FTouchIn.SliceCount == 0)
                {
                    browser.Host.SendMouseClickEvent(mouseEvent, CfxMouseButtonType.Left, true, 1);
                    isTouch = false;
                }



                // keyboard
                for (int i = 0; i < keyCode.Count; i++)
                {
                    if (!FKeyCodeIn.Contains(keyCode[i]))
                    {
                        CfxKeyEvent keyEvent = new CfxKeyEvent();

                        keyEvent.Type = CfxKeyEventType.Keyup;
                        keyEvent.WindowsKeyCode = keyCode[i];
                        keyEvent.NativeKeyCode = keyCode[i];

                        browser.Host.SendKeyEvent(keyEvent);

                        keyCode.RemoveAt(i);
                        i--;
                    }
                }

                if (FKeyCharIn.SliceCount > 0 && FKeyCodeIn.SliceCount > 0)
                {
                    int count = Math.Max(FKeyCharIn.SliceCount, FKeyCodeIn.SliceCount);
                    for (int i = 0; i < count; i++)
                    {
                        int code = FKeyCodeIn[i];
                        if (code >= 0 && FKeyEventTypeIn[i] != KeyNotificationKind.KeyUp && !keyCode.Contains(code))
                        {
                            CfxKeyEvent keyEvent = new CfxKeyEvent();

                            keyEvent.Type = CfxKeyEventType.Keydown;
                            keyEvent.WindowsKeyCode = code;
                            keyEvent.NativeKeyCode = code;

                            browser.Host.SendKeyEvent(keyEvent);
                        }

                        short ch = (short)(FKeyCharIn[i].Length > 0 ? FKeyCharIn[i][0] : 0);
                        if (ch > 0)
                        {
                            CfxKeyEvent keyEvent = new CfxKeyEvent();

                            keyEvent.Type = CfxKeyEventType.Char;
                            keyEvent.Character = ch;
                            keyEvent.UnmodifiedCharacter = ch;
                            keyEvent.WindowsKeyCode = ch;
                            keyEvent.NativeKeyCode = ch;

                            browser.Host.SendKeyEvent(keyEvent);
                        }
                    }
                }


                if (FReloadIn[0])
                {
                    browser.ReloadIgnoreCache();
                }

                if (zoomLevel != browser.Host.ZoomLevel)
                {
                    browser.Host.ZoomLevel = zoomLevel;
                }

                if (FUpdateDomIn[0])
                {
                    browser.MainFrame.GetSource(visitor);
                }

                if (isDocumentReady)
                {
                    if (FBindIn.IsChanged && FBindIn[0] && FMethodIn.SliceCount > 0)
                    {
                        BindFunctions(FObjectIn[0], FMethodIn.ToArray());
                    }

                    if (FScrollToIn.IsChanged)
                    {
                        scroll = FScrollToIn[0];

                        ExecuteJavascript(
                        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        @"window.scrollTo({0} *  document.body.scrollWidth, {1} * document.body.scrollHeight);",
                        scroll.x,
                        scroll.y
                        )
                        );
                    }
                }

                if (FExecuteIn[0])
                {
                    ExecuteJavascript(FJavaScriptIn[0]);
                }

                if (FEvaluateJavaScriptIn[0])
                {
                    EvaluateJavascript(FJavaScriptIn[0], (value, exception) =>
                    {
                        FResultJSOut[0] = CfrV8ValueToString(value);
                    });
                }

            }


            if (FWidthIn.IsChanged || FHeightIn.IsChanged)
            {
                if (FWidthIn.SliceCount > 0 && FHeightIn.SliceCount > 0 && FWidthIn[0] > 0 && FHeightIn[0] > 0)
                {
                    lock (bLock)
                    {
                        width = FWidthIn[0];
                        height = FHeightIn[0];

                        image = new byte[width * height * 4];
                        isImageReady = false;
                    }

                    if (browser != null) browser.Host.WasResized();

                    invalidate = true;
                }
            }

            FIsLoadingOut[0] = !isDocumentReady;

            this.Mouse = FMouseIn[0];
            this.Keyboard = FKeyboardIn[0];

            //CfxRuntime.DoMessageLoopWork();
        }

        void displayHandler_OnConsoleMessage(object sender, CfxOnConsoleMessageEventArgs e)
        {
            if (FConsoleIn[0])
            {
                FLogger.Log(LogType.Message, string.Format("{0} ({1}:{2})", e.Message, e.Source, e.Line));
            }
            e.SetReturnValue(false);
        }

        void requestHandler_OnBeforeResourceLoad(object sender, CfxOnBeforeResourceLoadEventArgs e)
        {
            List<string[]> hdrMap = e.Request.GetHeaderMap();
            if (FUserAgentIn[0] != "")
            {
                foreach (string[] keyValue in hdrMap)
                {
                    if (keyValue[0] == "User-Agent")
                    {
                        keyValue[1] = FUserAgentIn[0];
                    }
                }
            }
            e.Request.SetHeaderMap(hdrMap);
            e.SetReturnValue(CfxReturnValue.Continue);
        }


        static string CfrV8ValueToString(CfrV8Value val)
        {
            if (val.IsBool)
                return val.BoolValue.ToString(CultureInfo.InvariantCulture);
            else if (val.IsDouble)
                return val.DoubleValue.ToString(CultureInfo.InvariantCulture);
            else if (val.IsInt)
                return val.IntValue.ToString(CultureInfo.InvariantCulture);
            else if (val.IsUint)
                return val.UintValue.ToString(CultureInfo.InvariantCulture);
            else if (val.IsString)
                return val.StringValue;

            else if (val.IsArray)
            {
                string[] arr = new string[val.ArrayLength];
                for (int k = 0; k < val.ArrayLength; k++) arr[k] = CfrV8ValueToString(val.GetValue(k));
                return "[" + string.Join(",", arr) + "]";
            }

            else return "";
        }

        void visitor_Visit(object sender, CfxStringVisitorVisitEventArgs e)
        {
            FRootElementOut[0] = HtmlToXElement(e.String);
            FDomOut[0] = new XDocument(FRootElementOut[0]);
        }

        bool BindFunctions(string objName, string[] funcNames)
        {
            if (remoteBrowser == null) return false;
            try
            {
                var ctx = remoteBrowser.CreateRemoteCallContext();
                ctx.Enter();
                try
                {
                    // if (v8Handler != null) v8Handler.Dispose(); // unbind

                    v8Handler = new CfrV8Handler();
                    v8Handler.Execute += new CfrV8HandlerExecuteEventHandler(JavascriptCallback);

                    CfrTaskRunner.GetForThread(CfxThreadId.Renderer).PostTask(new BindFunctionsTask(remoteBrowser, objName, funcNames, v8Handler));

                    return true;
                }
                finally
                {
                    ctx.Exit();
                }
            }
            catch (System.IO.IOException)
            {
                return false;
            }
        }

        class BindFunctionsTask : CfrTask
        {
            internal BindFunctionsTask(CfrBrowser remoteBrowser, string objName, string[] funcNames, CfrV8Handler v8Handler)
            {
                this.Execute += (s, e) =>
                {
                    var context = remoteBrowser.MainFrame.V8Context;
                    context.Enter();

                    CfrV8Value obj = null;

                    if (objName != null && objName != "" && !context.Global.HasValue(objName))
                    {
                        obj = CfrV8Value.CreateObject(new CfrV8Accessor());
                        context.Global.SetValue(objName, obj, CfxV8PropertyAttribute.DontDelete | CfxV8PropertyAttribute.ReadOnly);
                        //context.Global.DeleteValue(objName);
                    }

                    foreach (string name in funcNames)
                    {
                        // bind
                        CfrV8Value func = CfrV8Value.CreateFunction(name, v8Handler);

                        if (obj != null && !obj.HasValue(name))
                        {
                            obj.SetValue(name, func, CfxV8PropertyAttribute.DontDelete | CfxV8PropertyAttribute.ReadOnly);
                        }
                        else if (!context.Global.HasValue(name))
                        {
                            context.Global.SetValue(name, func, CfxV8PropertyAttribute.DontDelete | CfxV8PropertyAttribute.ReadOnly);
                        }
                    }
                    context.Exit();
                };
            }
        }

        private void JavascriptCallback(object sender, CfrV8HandlerExecuteEventArgs e)
        {
            if (e.Arguments.Length == 0) return;

            int index = FMethodIn.IndexOf(e.Name);
            if (index >= 0)
            {
                string[] arr = new string[e.Arguments.Length];
                for (int k = 0; k < e.Arguments.Length; k++) arr[k] = CfrV8ValueToString(e.Arguments[k]);
                FResultOut[index] = string.Join(",", arr);
                method[index] = true;
            }

        }


        bool ExecuteJavascript(string code)
        {
            if (browser != null)
            {
                browser.MainFrame.ExecuteJavaScript(code, null, 0);
                return true;
            }
            else
            {
                return false;
            }
        }

        bool EvaluateJavascript(string code, Action<CfrV8Value, CfrV8Exception> callback)
        {
            if (remoteBrowser == null) return false;
            try
            {
                var ctx = remoteBrowser.CreateRemoteCallContext();
                ctx.Enter();
                try
                {
                    CfrTaskRunner.GetForThread(CfxThreadId.Renderer).PostTask(new EvaluateTask(remoteBrowser, code, callback));
                    return true;
                }
                finally
                {
                    ctx.Exit();
                }
            }
            catch (System.IO.IOException)
            {
                return false;
            }

        }

        class EvaluateTask : CfrTask
        {
            CfrBrowser browser;
            string code;
            Action<CfrV8Value, CfrV8Exception> callback;

            internal EvaluateTask(CfrBrowser browser, string code, Action<CfrV8Value, CfrV8Exception> callback)
            {
                this.browser = browser;
                this.code = code;
                this.callback = callback;
                this.Execute += (s, e) =>
                {
                    Task_Execute(e);
                };
            }

            void Task_Execute(CfrEventArgs e)
            {
                bool evalSucceeded = false;
                try
                {
                    CfrV8Value retval;
                    CfrV8Exception ex;
                    var context = browser.MainFrame.V8Context;
                    var result = context.Eval(code, out retval, out ex);

                    evalSucceeded = true;
                    if (result)
                    {
                        callback(retval, ex);
                    }
                    else
                    {
                        callback(null, null);
                    }

                }
                catch
                {
                    if (!evalSucceeded)
                        callback(null, null);
                }
            }
        }


        public void OnLoadStart()
        {
            isDocumentReady = false;
        }

        public void OnLoadEnd()
        {
            EvaluateJavascript("[document.body.scrollWidth,document.body.scrollHeight]", (value, exception) =>
            {
                if (value != null)
                {
                    FDocumentWidthOut[0] = value.GetValue(0).IntValue;
                    FDocumentHeightOut[0] = value.GetValue(1).IntValue;

                    if (FLivePageIn[0]) ExecuteJavascript(LIVEPAGE_LOAD_FUNC);

                    isDocumentReady = true;
                }
            });

            browser.Host.SendFocusEvent(true); // show cursor caret
        }

        void lifeSpanHandler_OnBeforePopup(object sender, Chromium.Event.CfxOnBeforePopupEventArgs e)
        {
            if (!FPopupIn[0])
            {
                browser.MainFrame.LoadUrl(e.TargetUrl);
                e.SetReturnValue(true);
            }
            else
            {
                e.SetReturnValue(false);
            }
        }

        void loadHandler_OnLoadingStateChange(object sender, Chromium.Event.CfxOnLoadingStateChangeEventArgs e)
        {
            FCurrentUrlOut[0] = browser.MainFrame.Url;
        }


        private void requestHandler_OnBeforeBrowse(object sender, Chromium.Event.CfxOnBeforeBrowseEventArgs e)
        {
            // url filter
            bool filter = false;
            for (int i = 0; i < FFilterUrlIn.SliceCount; i++)
            {
                if (FFilterUrlIn[i] == "" || e.Browser.MainFrame.Url.Contains(FFilterUrlIn[i])) filter = true;
            }
            e.SetReturnValue(!filter);
        }

        void renderHandler_GetViewRect(object sender, Chromium.Event.CfxGetViewRectEventArgs e)
        {
            e.Rect.X = 0;
            e.Rect.Y = 0;
            e.Rect.Width = width;
            e.Rect.Height = height;
            e.SetReturnValue(true);
        }

        void renderHandler_GetRootScreenRect(object sender, CfxGetRootScreenRectEventArgs e)
        {
            e.Rect.X = 0;
            e.Rect.Y = 0;
            e.Rect.Width = width;
            e.Rect.Height = height;
            e.SetReturnValue(true);
        }


        void loadHandler_OnLoadError(object sender, Chromium.Event.CfxOnLoadErrorEventArgs e)
        {
            if (e.ErrorCode == CfxErrorCode.Aborted)
            {
                FErrorTextOut[0] = e.ErrorText;
            }
        }

        void renderHandler_OnPaint(object sender, Chromium.Event.CfxOnPaintEventArgs e)
        {
            if (image != null && e.Width == width && e.Height == height && FEnabledIn[0])
            {
                lock (bLock)
                {
                    unsafe
                    {
                        fixed (byte* p = image)
                        {
                            memcpy((IntPtr)p, e.Buffer, (UIntPtr)(width * height * 4));
                        }
                    }
                    isImageReady = true;
                }

            }
        }
        public void SetRemoteBrowser(CfrBrowser remoteBrowser)
        {
            this.remoteBrowser = remoteBrowser;
        }

        public virtual void Start()
        {
        }

        public void LoadString(string str)
        {
            if (browser != null)
            {
                string local = "about:blank";

                browser.MainFrame.LoadUrl(local);
                browser.MainFrame.LoadString(str, local);
            }
        }
        public void LoadUrl(string url)
        {
            if (browser != null)
            {
                browser.MainFrame.LoadUrl(url);
            }
        }

        void lifeSpanHandler_OnAfterCreated(object sender, Chromium.Event.CfxOnAfterCreatedEventArgs e)
        {
            browser = e.Browser;

            HTMLTextureNode.nodes[browser.Identifier] = this;

            Start();

            browser.Host.WasResized();

            invalidate = true;
        }

        public static XElement HtmlToXElement(string html)
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.OptionOutputAsXml = true;
            doc.LoadHtml(html);
            using (StringWriter writer = new StringWriter())
            {
                doc.Save(writer);
                using (StringReader reader = new StringReader(writer.ToString()))
                {
                    return XElement.Load(reader);
                }
            }
        }

        public void Update(DX11RenderContext context)
        {

            if ((isImageReady && invalidate) || !this.FTextureOutput[0].Contains(context))
            {
                if (this.FTextureOutput[0].Contains(context)) this.FTextureOutput[0].Dispose(context);

                texture = new DX11DynamicTexture2D(context, width, height, SlimDX.DXGI.Format.B8G8R8A8_UNorm);
                this.FTextureOutput[0][context] = texture;
                invalidate = false;
            }

            if (browser != null && image != null && isImageReady && FEnabledIn[0])
            {
                lock (bLock)
                {
                    unsafe
                    {
                        fixed (byte* p = image)
                        {
                            texture.WriteDataPitch((IntPtr)p, width * height * 4);
                        }
                    }
                }
            }
        }

        public void Destroy(FeralTic.DX11.DX11RenderContext context, bool force)
        {
            this.FTextureOutput[0].Dispose(context);
        }

        public void Dispose()
        {
            browser.Host.CloseBrowser(true);

            if (this.FTextureOutput[0] != null)
            {
                this.FTextureOutput[0].Dispose();
            }

            if (FMouseSubscription != null)
            {
                FMouseSubscription.Dispose();
                FMouseSubscription = null;
            }

            if (FKeyboardSubscription != null)
            {
                FKeyboardSubscription.Dispose();
                FKeyboardSubscription = null;
            }
        }

    }
}
