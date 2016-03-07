// Licensed under the Apache License, Version 2.0
// http://www.apache.org/licenses/LICENSE-2.0

using Fclp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace WebScreen
{
    class Program
    {
        private BootStrap _bootstrap;

        static void Main (string[] args)
        {
            try
            {
                var prop = ApplicationArguments.Parse (args);
                if (prop.PrintHelp)
                {
                    return;
                }

                var bootstrap = new BootStrap (prop);
                bootstrap.Start ();

                var program = new Program (bootstrap);
                program.Run ();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine (ex.Message);
                Console.Error.WriteLine (ex.StackTrace);
            }
        }

        private Program (BootStrap bootstrap)
        {
            _bootstrap = bootstrap;
        }

        public void Run ()
        {
            foreach (var webPageUrl in _bootstrap.Hosts)
            {
                Console.WriteLine ("Visiting " + webPageUrl + "...");

                using (var browser = FirefoxBrowser.Start (webPageUrl, _bootstrap.RoutesLayer))
                {
                    Thread.Sleep (TimeSpan.FromSeconds (10));

                    using (var screenshot = browser.TakeScreenshot ())
                    {
                        var fileName = Regex.Replace (webPageUrl, @"[^\w\.]", string.Empty) + ".png";
                        var filePath = Path.Combine (_bootstrap.ScreenshotDirectory, fileName);
                        screenshot.Save (filePath);
                    }
                }
            }
        }

        private class BootStrap
        {
            private static readonly List<string> DefaultHosts = new List<string> {
                                                               "google.com",
                                                               "facebook.com",
                                                               "youtube.com",
                                                               "baidu.com",
                                                               "yahoo.com",
	                                                           "amazon.com",
	                                                           "wikipedia.org",
	                                                           "qq.com",
	                                                           "google.co.in",
	                                                           "twitter.com",
	                                                           "live.com"
                                                           };

            private ApplicationArguments _args;

            public BootStrap (ApplicationArguments args)
            {
                _args = args;
            }

            public void Start ()
            {
                Hosts = GetHosts ();
                ScreenshotDirectory = Environment.ExpandEnvironmentVariables (_args.ScreenshotDirectory);

                Directory.CreateDirectory (ScreenshotDirectory);
            }

            public IReadOnlyCollection<string> Hosts { get; private set; }

            public string ScreenshotDirectory { get; private set; }

            public string RoutesLayer { get { return _args.RoutesLayer; } }

            private IReadOnlyCollection<string> GetHosts ()
            {
                if (string.IsNullOrEmpty (_args.HostsFile))
                {
                    return DefaultHosts.AsReadOnly ();
                }

                var hostFilePath = Environment.ExpandEnvironmentVariables (_args.HostsFile);
                return File.ReadAllLines (hostFilePath)
                    .Where (line => !string.IsNullOrWhiteSpace (line))
                    .Select (line => line.Trim ())
                    .ToList ()
                    .AsReadOnly ();
            }
        }

        private sealed class ApplicationArguments
        {
            public bool PrintHelp { get; set; }
            public string HostsFile { get; set; }
            public string RoutesLayer { get; set; }
            public string ScreenshotDirectory { get; set; }

            public static ApplicationArguments Parse (string[] args)
            {
                var p = new FluentCommandLineParser<ApplicationArguments> ();

                p.Setup (arg => arg.HostsFile)
                 .As ("pagesFile")
                 .WithDescription ("File path to the list of web pages to visit");

                p.Setup (arg => arg.ScreenshotDirectory)
                 .As ("screenshotsDir")
                 .SetDefault ("./screenshots")
                 .WithDescription ("Directory where screenshots should be saved");

                p.Setup (arg => arg.RoutesLayer)
                 .As ("routesLayer")
                 .SetDefault ("turbobrowsers/block-ad-routes")
                 .WithDescription ("Routes layer to start mozilla/firefox-base with");

                p.SetupHelp ("?", "help")
                 .Callback (text => Console.WriteLine (text));

                var result = p.Parse (args);

                if (result.HasErrors)
                {
                    throw new InvalidOperationException (result.ErrorText);
                }

                var arguments = p.Object;
                arguments.PrintHelp = result.HelpCalled;
                return arguments;
            }
        }

        private sealed class FirefoxBrowser : IDisposable
        {
            private Process _browserProcess;
            private Window _mainWindow;

            public static FirefoxBrowser Start (string webPageUrl, string routeBlockLayer, int maxRetry = 32)
            {
                var browserProcess = StartFirefox (webPageUrl, routeBlockLayer);

                var currentRetry = 0;
                while (currentRetry < maxRetry)
                {
                    ++currentRetry;

                    var control = new Control (AutomationElement.RootElement);
                    var firefoxWindows = control.Find<Window> (ControlType.Window, className: "MozillaWindowClass").ToList ();
                    if (firefoxWindows.Count > 1)
                    {
                        throw new InvalidOperationException ("More than 1 browser window found");
                    }

                    if (firefoxWindows.Any ())
                    {
                        var window = firefoxWindows.First ();
                        return new FirefoxBrowser (window, browserProcess);
                    }

                    Thread.Sleep (TimeSpan.FromSeconds (1));
                }

                throw new InvalidOperationException ("Browser window not found");
            }

            private static Process StartFirefox (string webPageUrl, string routeBlockLayer)
            {
                var startInfo = new ProcessStartInfo ("turbo.exe")
                {
                    Arguments = "try " + routeBlockLayer + ",mozilla/firefox-base -- " + webPageUrl
                };
                return Process.Start (startInfo);
            }

            private FirefoxBrowser (Window mainWindow, Process browserProcess)
            {
                _mainWindow = mainWindow;
                _browserProcess = browserProcess;
            }

            public Bitmap TakeScreenshot ()
            {
                _mainWindow.Maximize ();

                Thread.Sleep (TimeSpan.FromSeconds (3));

                return TakeScreenshot (_mainWindow.BoundingRectangle);
            }

            private Bitmap TakeScreenshot (Rect area)
            {
                var width = (int)area.Width;
                var height = (int)area.Height;

                var bitmap = new Bitmap (width, height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage (bitmap))
                {
                    graphics.CopyFromScreen ((int)area.Left, (int)area.Top, 0, 0, new System.Drawing.Size (width, height), CopyPixelOperation.SourceCopy);
                }
                return bitmap;
            }

            public void Dispose ()
            {
                _mainWindow.Close ();

                _browserProcess.Kill ();
                _browserProcess.WaitForExit ();
            }
        }
    }
}
