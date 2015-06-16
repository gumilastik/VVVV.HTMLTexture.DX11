#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using VVVV.Core.Logging;
#endregion usings

using Awesomium.Core;

using FeralTic.DX11;
using FeralTic.DX11.Resources;

using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using VVVV.Utils.IO;
using VVVV.Utils.Win32;

using System.Xml.Linq;

using HtmlAgilityPack;
using System.IO;
/*
copy

awesomium_process.exe
awesomium.dll
icudt.dll
libEGL.dll
libGLESv2.dll
xinput9_1_0.dll
avcodec-53.dll
avformat-53.dll
avutil-51.dll
*/

/*
alternatives:
1.
https://bitbucket.org/geckofx/geckofx-33.0/src - OffScreenGeckoWebBrowser
http://www.softez.pp.ua/2014/01/28/gecko-%D0%B8-csharp-geckofx/
xulrunner

2.
http://joelverhagen.com/blog/2013/12/headless-chromium-in-c-with-cefglue/
cef browser path replace
*/

/*
TODO:
scrollWidth не всегда есть на странице?
*/

namespace VVVV.DX11.Nodes
{
	[Startable]
	public class HTMLTextureStartable : IStartable
	{
		// Main entry point when called by vvvv
		void IStartable.Start()
		{
			if(!WebCore.IsInitialized)
			{
				WebCore.Initialize(new WebConfig()
				{
					LogPath = Environment.CurrentDirectory + "/awesomium.log",
					LogLevel = LogLevel.Verbose,
				});
				
			}
		}
		
		void IStartable.Shutdown()
		{
			WebCore.Shutdown();
		}
	}
	
	
	#region PluginInfo
	[PluginInfo(Name = "HTMLTexture", AutoEvaluate = true, Category = "DX11.Texture", Version = "URL", Help = "", Tags = "browser, Awesomium")]
	#endregion PluginInfo
	public class URLDX11_TextureHTMLTextureNode : HTMLTextureNode, IPluginEvaluate
	{
		[Input("Url", DefaultString = DEFAULT_URL)]
		public ISpread<string> FUrlIn;
		
		public const string DEFAULT_URL = "http://vvvv.org";
		
		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			Update(SpreadMax);
			if (FUrlIn.IsChanged)
			{
				view.Source = FUrlIn[0].ToUri();
				view.FocusView();
			}
		}
	}
	
	#region PluginInfo
	[PluginInfo(Name = "HTMLTexture", AutoEvaluate = true, Category = "DX11.Texture", Version = "String", Help = "", Tags = "browser, Awesomium")]
	#endregion PluginInfo
	public class StringDX11_TextureHTMLTextureNode : HTMLTextureNode, IPluginEvaluate
	{
		[Input("HTML", DefaultString = @"<html><head></head><body bgcolor=""#ffffff""></body></html>")]
		public ISpread<string> FHtmlIn;
		
		public void Evaluate(int SpreadMax)
		{
			Update(SpreadMax);
			if (FHtmlIn.IsChanged)
			{
				view.LoadHTML(FHtmlIn[0]);
				view.FocusView();
			}
		}
	}
	
	public abstract class HTMLTextureNode : IDX11ResourceProvider, IDisposable
	{
		public const int DEFAULT_WIDTH = 640;
		public const int DEFAULT_HEIGHT = 480;
		
		// JavaScript that will get a reliable value
		// for the full height of the document loaded.
		public const string PAGE_HEIGHT_FUNC = "(function() { " +
		"var bodyElmnt = document.body; var html = document.documentElement; " +
		"var height = Math.max( bodyElmnt.scrollHeight, bodyElmnt.offsetHeight, html.clientHeight, html.scrollHeight, html.offsetHeight ); " +
		"return height; })();";
		
		#region fields & pins
		[Input("Reload", IsBang = true)]
		public ISpread<bool> FReloadIn;
		
		[Input("Width", DefaultValue = DEFAULT_WIDTH)]
		public IDiffSpread<int> FWidthIn;
		
		[Input("Height", DefaultValue = DEFAULT_HEIGHT)]
		public IDiffSpread<int> FHeightIn;
		
		[Input("Popup")]
		public IDiffSpread<bool> FPopupIn;
		
		[Input("Transparent")]
		public IDiffSpread<bool> FTransparentIn;
		
		[Input("Zoom Level", DefaultValue = 1.0)]
		public IDiffSpread<double> FZoomLevelIn;
		
		[Input("Mouse Event")]
		public ISpread<Mouse> FMouseIn;
		
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
		
		[Input("Enabled", DefaultValue = 1)]
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
		private int width;
		private int height;
		private Vector2D scroll;
		private bool invalidate;
		private bool[] method;
		
		protected WebView view;
		private WebSession session;
		private BitmapSurface surface;
		
		DX11DynamicTexture2D texture;
		
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
						if(FEnabledIn[0])
						{
							var mouseButtonNotification = n as MouseButtonNotification;
							Awesomium.Core.MouseButton mouseButton = MouseButton.Left;
							
							if (n.Kind == MouseNotificationKind.MouseUp || n.Kind == MouseNotificationKind.MouseDown)
							{
								switch (mouseButtonNotification.Buttons)
								{
									case MouseButtons.Left:
									mouseButton = MouseButton.Left;
									break;
									case MouseButtons.Middle:
									mouseButton = MouseButton.Middle;
									break;
									case MouseButtons.Right:
									mouseButton = MouseButton.Right;
									break;
									default:
									mouseButton = MouseButton.Left;
									break;
								}
							}
							
							
							switch (n.Kind)
							{
								case MouseNotificationKind.MouseDown:
								{
									view.InjectMouseDown(mouseButton);
								}
								break;
								case MouseNotificationKind.MouseUp:
								{
									view.InjectMouseUp(mouseButton);
								}
								break;
								case MouseNotificationKind.MouseMove:
								{
									view.InjectMouseMove(
									(int)VMath.Map(n.Position.X, 0, n.ClientArea.Width, 0, width, TMapMode.Clamp),
									(int)VMath.Map(n.Position.Y, 0, n.ClientArea.Height, 0, height, TMapMode.Clamp)
									);
								}
								break;
								case MouseNotificationKind.MouseWheel:
								{
									var mouseWheel = n as MouseWheelNotification;
									var wheel = FMouseWheel;
									FMouseWheel += mouseWheel.WheelDelta;
									var delta = (int)Math.Round((float)(FMouseWheel - wheel) / Const.WHEEL_DELTA);
									
									view.InjectMouseWheel(mouseWheel.WheelDelta, 0);
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
		
		public static VirtualKey MapKeys(Keys key)
		{
			switch (key)
			{
				case Keys.Back: return VirtualKey.BACK;
				case Keys.Delete: return VirtualKey.DELETE;
				case Keys.Tab: return VirtualKey.TAB;
				case Keys.OemClear: return VirtualKey.CLEAR;
				case Keys.Enter: return VirtualKey.RETURN;
				case Keys.Pause: return VirtualKey.PAUSE;
				case Keys.Escape: return VirtualKey.ESCAPE;
				case Keys.Space: return VirtualKey.SPACE;
				case Keys.NumPad0: return VirtualKey.NUMPAD0;
				case Keys.NumPad1: return VirtualKey.NUMPAD1;
				case Keys.NumPad2: return VirtualKey.NUMPAD2;
				case Keys.NumPad3: return VirtualKey.NUMPAD3;
				case Keys.NumPad4: return VirtualKey.NUMPAD4;
				case Keys.NumPad5: return VirtualKey.NUMPAD5;
				case Keys.NumPad6: return VirtualKey.NUMPAD6;
				case Keys.NumPad7: return VirtualKey.NUMPAD7;
				case Keys.NumPad8: return VirtualKey.NUMPAD8;
				case Keys.NumPad9: return VirtualKey.NUMPAD9;
				case Keys.Up: return VirtualKey.UP;
				case Keys.Down: return VirtualKey.DOWN;
				case Keys.Right: return VirtualKey.RIGHT;
				case Keys.Left: return VirtualKey.LEFT;
				case Keys.Insert: return VirtualKey.INSERT;
				case Keys.Home: return VirtualKey.HOME;
				case Keys.End: return VirtualKey.END;
				case Keys.PageUp: return VirtualKey.PRIOR;
				case Keys.PageDown: return VirtualKey.NEXT;
				case Keys.F1: return VirtualKey.F1;
				case Keys.F2: return VirtualKey.F2;
				case Keys.F3: return VirtualKey.F3;
				case Keys.F4: return VirtualKey.F4;
				case Keys.F5: return VirtualKey.F5;
				case Keys.F6: return VirtualKey.F6;
				case Keys.F7: return VirtualKey.F7;
				case Keys.F8: return VirtualKey.F8;
				case Keys.F9: return VirtualKey.F9;
				case Keys.F10: return VirtualKey.F10;
				case Keys.F11: return VirtualKey.F11;
				case Keys.F12: return VirtualKey.F12;
				case Keys.F13: return VirtualKey.F13;
				case Keys.F14: return VirtualKey.F14;
				case Keys.F15: return VirtualKey.F15;
				
				
				case Keys.A: return VirtualKey.A;
				case Keys.B: return VirtualKey.B;
				case Keys.C: return VirtualKey.C;
				case Keys.D: return VirtualKey.D;
				case Keys.E: return VirtualKey.E;
				case Keys.F: return VirtualKey.F;
				case Keys.G: return VirtualKey.G;
				case Keys.H: return VirtualKey.H;
				case Keys.I: return VirtualKey.I;
				case Keys.J: return VirtualKey.J;
				case Keys.K: return VirtualKey.K;
				case Keys.L: return VirtualKey.L;
				case Keys.M: return VirtualKey.M;
				case Keys.N: return VirtualKey.N;
				case Keys.O: return VirtualKey.O;
				case Keys.P: return VirtualKey.P;
				case Keys.Q: return VirtualKey.Q;
				case Keys.R: return VirtualKey.R;
				case Keys.S: return VirtualKey.S;
				case Keys.T: return VirtualKey.T;
				case Keys.U: return VirtualKey.U;
				case Keys.V: return VirtualKey.V;
				case Keys.W: return VirtualKey.W;
				case Keys.X: return VirtualKey.X;
				case Keys.Y: return VirtualKey.Y;
				case Keys.Z: return VirtualKey.Z;
				case Keys.CapsLock: return VirtualKey.CAPITAL;
				case Keys.RShiftKey: return VirtualKey.RSHIFT;
				case Keys.LShiftKey: return VirtualKey.LSHIFT;
				case Keys.RControlKey: return VirtualKey.RCONTROL;
				case Keys.LControlKey: return VirtualKey.LCONTROL;
				case Keys.Alt: return VirtualKey.RMENU;
				case Keys.LWin: return VirtualKey.LWIN;
				case Keys.RWin: return VirtualKey.RWIN;
				case Keys.Help: return VirtualKey.HELP;
				case Keys.Print: return VirtualKey.PRINT;
				default: return VirtualKey.UNKNOWN;
			}
		}
		
		
		static private Modifiers MapModifiers(Keys key)
		{
			int modifiers = 0;
			
			if (key == Keys.Control)
			modifiers |= (int)Modifiers.ControlKey;
			
			if (key == Keys.Shift)
			modifiers |= (int)Modifiers.ShiftKey;
			
			if (key == Keys.Alt)
			modifiers |= (int)Modifiers.AltKey;
			
			return (Modifiers)modifiers;
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
					if(FEnabledIn[0])
					{
						WebKeyboardEvent keyEvent = new WebKeyboardEvent();
						keyEvent.Modifiers = MapModifiers(FKeyboard.Modifiers);
						
						switch (n.Kind)
						{
							case KeyNotificationKind.KeyDown:
							{
								var keyDown = n as KeyDownNotification;
								
								keyEvent.Type = WebKeyboardEventType.KeyDown;
								keyEvent.NativeKeyCode = (int)keyDown.KeyCode;
								keyEvent.VirtualKeyCode = MapKeys(keyDown.KeyCode);
							}
							break;
							case KeyNotificationKind.KeyPress:
							{
								var keyPress = n as KeyPressNotification;
								//FLogger.Log(LogType.Debug, " " + keyPress.KeyChar );
								keyEvent.Type = WebKeyboardEventType.Char;
								keyEvent.Text = keyPress.KeyChar.ToString();
								keyEvent.NativeKeyCode = (int)keyPress.KeyChar;
								//keyEvent.VirtualKeyCode = MapKeys((int)keyPress.KeyChar);
							}
							break;
							case KeyNotificationKind.KeyUp:
							{
								var keyUp = n as KeyUpNotification;
								
								keyEvent.Type = WebKeyboardEventType.KeyUp;
								keyEvent.NativeKeyCode = (int)keyUp.KeyCode;
								keyEvent.VirtualKeyCode = MapKeys(keyUp.KeyCode);
							}
							break;
							default:
							break;
						}
						
						view.InjectKeyboardEvent(keyEvent);
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
				session = WebCore.CreateWebSession(new WebPreferences()
				{
					FileAccessFromFileURL = true,
					UniversalAccessFromFileURL = true,
					SmoothScrolling = true,
					WebGL = true,
					EnableGPUAcceleration = true,

					JavascriptViewChangeSource = true,
					JavascriptViewEvents = true,
					JavascriptViewExecute = true,
					
					WebSecurity = false,
					
					//CustomCSS = "* { -webkit-user-select: none; }",
				});
				
				width = DEFAULT_WIDTH;
				height = DEFAULT_HEIGHT;
				
				view = WebCore.CreateWebView(width, height, session);//, WebViewType.Window);
				
				view.DocumentReady += view_DocumentReady;
				view.LoadingFrameComplete += view_LoadingFrameComplete;
				view.ShowCreatedWebView += view_ShowCreatedWebView;
				view.AddressChanged += view_AddressChanged;
				
				view.Crashed += view_Crashed;
				//view.AddressChanged
				
				view.ConsoleMessage += (s, e) =>
				{
					FLogger.Log(LogType.Debug, String.Format("Awesomium: {0}", e.Message));
				};
				
				invalidate = true;
				init = true;
			}
			
			if(FTransparentIn.IsChanged)
			{
				view.IsTransparent = FTransparentIn[0];
			}
			
			if(FReloadIn[0])
			{
				view.Reload(true);
			}
			
			if (FWidthIn.IsChanged || FHeightIn.IsChanged)
			{
				if(FWidthIn.SliceCount > 0 && FHeightIn.SliceCount>0 && FWidthIn[0] > 0 &&  FHeightIn[0] > 0)
				{
					width = FWidthIn[0];
					height = FHeightIn[0];
					view.Resize(width, height);
					invalidate = true;
				}
			}
			
			if(FMethodIn.IsChanged)
			{
				if(method == null || method.Length != FMethodIn.SliceCount) method = new bool[FMethodIn.SliceCount];
				FResultOut.SliceCount = FMethodIn.SliceCount;
				FMethodOut.SliceCount = FMethodIn.SliceCount;
			}
			
			
			for (int i = 0; i < FMethodOut.SliceCount; i++)
			{
				FMethodOut[i] = method[i];
				method[i] = false;
			}
			
			if (FBindIn.IsChanged)
			{
				for (int i = 0; i < SpreadMax; i++)
				{
					if(FBindIn[i])
					{
						using(JSObject jsobject = view.CreateGlobalJavascriptObject(FObjectIn[i] ))
						{
							jsobject.Bind(FMethodIn[i], JavascriptCallback);
						}
					}
				}
			}
			
			if(FExecuteIn[0])
			{
				JSValue val = (JSValue )view.ExecuteJavascriptWithResult(FJavaScriptIn[0]);
				FResultJSOut[0] = val.ToString();
			}
			
			if(FUpdateDomIn[0])
			{
				FRootElementOut[0] = HtmlToXElement(view.HTML);
				FDomOut[0] = new XDocument(FRootElementOut[0]);
			}
			
			if(FZoomLevelIn.IsChanged)
			{
				view.Zoom = (int)VMath.Map(FZoomLevelIn[0], 0, 1, 10, 100, TMapMode.Clamp);
				
			}
			
			if (FScrollToIn.IsChanged)
			{
				if (view.IsDocumentReady)
				{
					scroll = FScrollToIn[0];
					
					view.ExecuteJavascript(
					string.Format(System.Globalization.CultureInfo.InvariantCulture,
					@"window.scrollTo({0} *  document.body.scrollWidth, {1} * document.body.scrollHeight);",
					scroll.x,
					scroll.y
					)
					);
				}
			}
			
			this.Mouse = FMouseIn[0];
			this.Keyboard = FKeyboardIn[0];
			
			FIsLoadingOut[0] = view.IsLoading || !view.IsDocumentReady;
		}
		
		private JSValue JavascriptCallback( object sender, JavascriptMethodEventArgs e )
		{
			for (int j = 0; j < FMethodIn.SliceCount; j++)
			{
				// MessageBox.Show("e.Arguments.Length " + e.Arguments.Length);
				if(e.MethodName == FMethodIn[j])
				{
					if(e.Arguments.Length>0)
					{
						string [] arr = new string [e.Arguments.Length];
						for (int k = 0; k < e.Arguments.Length; k++) arr[k] = e.Arguments[k];
						FResultOut[j] = string.Join(",", arr); // e.Arguments[0];
					}
					method[j] = true;
					break;
				}
			}
			return true;
		}
		
		private void view_AddressChanged( object sender, UrlEventArgs e )
		{
			// Reflect the current URL to the window text.
			// Normally, after the page loads, we will get a title.
			// But a page may as well not specify a title.
			FCurrentUrlOut[0] = e.Url.ToString();
		}
		
		private void view_Crashed(object sender, CrashedEventArgs e)
		{
			FErrorTextOut[0] = e.Status.ToString();
		}
		
		
		private void view_ShowCreatedWebView( object sender, ShowCreatedWebViewEventArgs e )
		{
			if ( ( view == null ) || !view.IsLive )
			return;
			
			// Let the new view be destroyed. It is important to set Cancel to true
			// if you are not wrapping the new view, to avoid keeping it alive along
			// with a reference to its parent.
			
			// Load the url to the existing view.
			if(FPopupIn[0])
			{
				view.Source = e.TargetURL;
				e.Cancel = true;
			}
		}
		
		private void view_DocumentReady(object sender, DocumentReadyEventArgs e)
		{
			if ( e.ReadyState != DocumentReadyState.Loaded )
			return;
			
			// disable selection after loading page
			view.ExecuteJavascript( "document.body.onselectstart = function() { return false; }" );
			
			// parse html
			FRootElementOut[0] = HtmlToXElement(view.HTML);
			FDomOut[0] = new XDocument(FRootElementOut[0]);
			
			FDocumentWidthOut[0] = (int)view.ExecuteJavascriptWithResult("document.body.scrollWidth");
			FDocumentHeightOut[0] = (int)view.ExecuteJavascriptWithResult("document.body.scrollHeight");
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
		
		private void view_LoadingFrameComplete(object sender, FrameEventArgs e)
		{
			//FLogger.Log(LogType.Debug, "view_LoadingFrameComplete!  " );
			
			if (surface == null)
			{
				surface = (BitmapSurface)view.Surface;
				surface.Updated += surface_Updated;
			}
		}
		
		public void Update(IPluginIO pin, DX11RenderContext context)
		{
			if (this.invalidate || !this.FTextureOutput[0].Contains(context))
			{
				if (this.FTextureOutput[0].Contains(context)) this.FTextureOutput[0].Dispose(context);
				
				texture = new DX11DynamicTexture2D(context, width, height, SlimDX.DXGI.Format.B8G8R8A8_UNorm);
				this.FTextureOutput[0][context] = texture;
				invalidate = false;
			}
			
			if (surface != null && surface.IsDirty && FEnabledIn[0])
			{
				// update here!
				texture.WriteDataPitch(surface.Buffer, width * height * 4);
			}
			
		}
		
		void surface_Updated(object sender, SurfaceUpdatedEventArgs e)
		{
			//BitmapSurface surface = (BitmapSurface)sender;
			//texture.WriteDataPitch(surface.Buffer, width * height * 4);
			// FLogger.Log(LogType.Debug, "surface_Updated!  " + view.Width);
		}
		
		public void Destroy(IPluginIO pin, FeralTic.DX11.DX11RenderContext context, bool force)
		{
			this.FTextureOutput[0].Dispose(context);
		}
		
		public void Dispose()
		{
			if (this.FTextureOutput[0] != null)
			{
				this.FTextureOutput[0].Dispose();
			}
			
			view.Dispose();
			session.Dispose();
			
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
