// Licensed under the Apache License, Version 2.0
// http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace WebScreen
{
    internal class Control
    {
        private readonly AutomationElement _control;
        private readonly string _id;
        private readonly string _name;
        private readonly string _category;
        private readonly ControlType _controlType;

        public Control (AutomationElement control)
        {
            _control = control;

            var rawId = _control.GetRuntimeId ();
            _id = (rawId != null) ? string.Join ("-", rawId) : "";
            _name = _control.GetName ();
            _controlType = _control.GetControlType ();
            _category = _control.GetLocalizedControlType ();
        }

        public string Id { get { return _id; } }

        public bool IsEnabled { get { return _control.IsEnabled (); } }

        public string Name { get { return _name; } }

        public string Category { get { return _category; } }

        public ControlType ControlType { get { return _controlType; } }

        public AutomationElement BaseControl { get { return _control; } }

        public void SetFocus ()
        {
            _control.SetFocus ();
        }

        public IEnumerable<T> Find<T> (ControlType controlType = null, string automationId = null, string name = null, string className = null) where T : Control
        {
            var condition = AutomationElementExtensions.GetCondition (controlType, automationId, name, className);
            return Find<T> (condition);
        }

        public IEnumerable<T> Find<T> (Condition condition)
        {
            var controls = _control.FindAll (TreeScope.Descendants, condition);
            return controls.OfType<AutomationElement> ().Select (Wrap).OfType<T> ().ToArray ();
        }

        public static Control Wrap (AutomationElement control)
        {
            var controlType = control.GetControlType ();
            if ((ControlType.Window.Equals (controlType) || ControlType.Pane.Equals (controlType)) && control.HasPattern (WindowPattern.Pattern))
            {
                return new Window (control);
            }
            return new Control (control);
        }
    }

    internal class Window : Control
    {
        public Window (AutomationElement control) : base (control) { }

        public void Close ()
        {
            BaseControl.GetPattern<WindowPattern> (WindowPattern.Pattern).Close ();
        }

        public bool IsOffScreen
        {
            get { return BaseControl.IsOffScreen (); }
        }

        public Rect BoundingRectangle
        {
            get { return BaseControl.GetBoundingRectangle (); }
        }

        public void Maximize ()
        {
            BaseControl.GetPattern<WindowPattern> (WindowPattern.Pattern).SetWindowVisualState (WindowVisualState.Maximized);
        }
    }

    internal static class ControlExtensions
    {
        private static Regex NoPrintableCharactersPattern = new Regex (@"[\W]", RegexOptions.Compiled);

        public static void CloseChildWindows (this Window mainWindow, CancellationToken cancelToken, int backoffMillis)
        {
            const int MaxCloseWindowsRetries = 2;

            var childWindows = mainWindow.Find<Window> (ControlType.Window).ToArray ();
            var attempt = -1;
            while (childWindows.Any () && attempt < MaxCloseWindowsRetries)
            {
                ++attempt;

                foreach (var childWindow in childWindows)
                {
                    childWindow.TryClose ();
                }

                cancelToken.WaitHandle.WaitOne (backoffMillis);

                childWindows = mainWindow.Find<Window> (ControlType.Window).ToArray ();
            }

            if (childWindows.Any ())
            {
                throw new InvalidOperationException ("Failed to close child windows in " + MaxCloseWindowsRetries + " attempts");
            }
        }

        public static void TryClose (this Window window)
        {
            try
            {
                window.Close ();

                Console.WriteLine (string.Format ("Closed window ({0})", window.Name));
            }
            catch (Exception ex)
            {
                Console.WriteLine (string.Format ("Failed to close window ({0}): {1}\n{2}",
                    window.Name,
                    ex.Message,
                    ex.StackTrace));
            }
        }

        private static string Sanitize (string controlName)
        {
            var sanitized = NoPrintableCharactersPattern.Replace (controlName, "");
            return sanitized.ToLowerInvariant ();
        }
    }

    internal static class AutomationElementExtensions
    {
        public static bool IsEnabled (this AutomationElement control)
        {
            return control.GetPropertyOrDefaultForStruct<bool> (AutomationElement.IsEnabledProperty, false);
        }

        public static bool HasKeyboardFocus (this AutomationElement control)
        {
            return control.GetPropertyOrDefaultForStruct<bool> (AutomationElement.HasKeyboardFocusProperty, false);
        }

        public static bool IsOffScreen (this AutomationElement control)
        {
            return control.GetPropertyOrDefaultForStruct<bool> (AutomationElement.IsOffscreenProperty, false);
        }

        public static string GetName (this AutomationElement control)
        {
            return GetPropertyOrDefaultForClass<string> (control, AutomationElement.NameProperty, "");
        }

        public static string GetLocalizedControlType (this AutomationElement control)
        {
            return GetPropertyOrDefaultForClass<string> (control, AutomationElement.LocalizedControlTypeProperty, "");
        }

        public static ControlType GetControlType (this AutomationElement control)
        {
            return control.GetPropertyOrDefaultForClass<ControlType> (AutomationElement.ControlTypeProperty, ControlType.Custom);
        }

        public static Rect GetBoundingRectangle (this AutomationElement control)
        {
            return control.GetPropertyOrDefaultForStruct<Rect> (AutomationElement.BoundingRectangleProperty, Rect.Empty);
        }

        public static bool HasPattern (this AutomationElement control, AutomationPattern pattern)
        {
            try
            {
                return control.GetCurrentPattern (pattern) != null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public static T GetPattern<T> (this AutomationElement control, AutomationPattern pattern) where T : BasePattern
        {
            var result = GetPatternOrNull<T> (control, pattern);
            if (result == null)
            {
                throw new InvalidOperationException ("Control does not support automation pattern " + pattern);
            }
            return result;
        }

        public static T GetPatternOrNull<T> (this AutomationElement control, AutomationPattern pattern) where T : BasePattern
        {
            object rawPattern;
            if (control.TryGetCurrentPattern (pattern, out rawPattern))
            {
                return rawPattern as T;
            }
            return null;
        }

        public static Condition GetCondition (ControlType controlType = null, string automationId = null, string name = null, string className = null)
        {
            var conditionDict = new Dictionary<AutomationProperty, object> ()
                {
                    {AutomationElement.ControlTypeProperty, controlType},
                    {AutomationElement.AutomationIdProperty, automationId},
                    {AutomationElement.NameProperty, name},
                    {AutomationElement.ClassNameProperty, className}
                };
            var conditions = conditionDict.Where (kvp => kvp.Value != null).Select (kvp => new PropertyCondition (kvp.Key, kvp.Value)).ToArray ();
            if (conditions.Length == 1)
            {
                return conditions.First ();
            }
            else if (conditions.Length > 1)
            {
                return new AndCondition (conditions);
            }
            return PropertyCondition.TrueCondition;
        }

        private static T GetPropertyOrDefaultForStruct<T> (this AutomationElement control, AutomationProperty property, T defaultValue) where T : struct
        {
            try
            {
                return (T)control.GetCurrentPropertyValue (property, ignoreDefaultValue: false);
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        private static T GetPropertyOrDefaultForClass<T> (this AutomationElement control, AutomationProperty property, T defaultValue) where T : class
        {
            try
            {
                var result = control.GetCurrentPropertyValue (property, ignoreDefaultValue: false) as T;
                return result != null ? result : defaultValue;
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }
    }
}
