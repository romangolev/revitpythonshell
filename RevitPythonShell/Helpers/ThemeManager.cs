using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml.Linq;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace RevitPythonShell.Helpers
{
    public enum Theme
    {
        Light,
        Dark
    }

    public class ThemeManager
    {
        private const string THEME_SETTING_NAME = "Theme";
        
        private static ThemeManager _instance;
        private Theme _currentTheme = Theme.Light;
        
        public static ThemeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ThemeManager();
                }
                return _instance;
            }
        }

        public Theme CurrentTheme
        {
            get { return _currentTheme; }
            private set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    OnThemeChanged();
                }
            }
        }

        public event EventHandler ThemeChanged;

        private ThemeManager()
        {
        }

        public void LoadThemeFromSettings(XDocument settings)
        {
            try
            {
                var themeElement = settings.Root.Element(THEME_SETTING_NAME);
                if (themeElement != null)
                {
                    string themeValue = themeElement.Value;
                    if (Enum.TryParse<Theme>(themeValue, out var theme))
                    {
                        CurrentTheme = theme;
                        return;
                    }
                }
            }
            catch (Exception)
            {
            }
            
            CurrentTheme = Theme.Light;
        }

        public void SaveThemeToSettings(XDocument settings)
        {
            var existingTheme = settings.Root.Element(THEME_SETTING_NAME);
            if (existingTheme != null)
            {
                existingTheme.Value = CurrentTheme.ToString();
            }
            else
            {
                settings.Root.Add(new XElement(THEME_SETTING_NAME, CurrentTheme.ToString()));
            }
        }

        public void SetTheme(Theme theme)
        {
            CurrentTheme = theme;
        }

        public void ToggleTheme()
        {
            SetTheme(CurrentTheme == Theme.Light ? Theme.Dark : Theme.Light);
        }

        private void OnThemeChanged()
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyThemeToWindow(Window window)
        {
            ResourceDictionary themeDictionary;
            
            if (CurrentTheme == Theme.Dark)
            {
                themeDictionary = new ResourceDictionary
                {
                    Source = new Uri("/RevitPythonShell;component/Themes/DarkTheme.xaml", UriKind.Relative)
                };
            }
            else
            {
                themeDictionary = new ResourceDictionary
                {
                    Source = new Uri("/RevitPythonShell;component/Themes/LightTheme.xaml", UriKind.Relative)
                };
            }

            window.Resources.MergedDictionaries.Clear();
            window.Resources.MergedDictionaries.Add(themeDictionary);
        }

        public IHighlightingDefinition GetHighlightingDefinition(Assembly assembly)
        {
            string resourceName = CurrentTheme == Theme.Dark
                ? "RevitPythonShell.Resources.Python-Dark.xshd"
                : "RevitPythonShell.Resources.Python.xshd";

            string highlightingName = CurrentTheme == Theme.Dark
                ? "Python-Dark Highlighting"
                : "Python Highlighting";

            IHighlightingDefinition existingHighlighting = HighlightingManager.Instance.GetDefinition(highlightingName);
            if (existingHighlighting != null)
            {
                return existingHighlighting;
            }

            using (Stream s = assembly.GetManifestResourceStream(resourceName))
            {
                if (s == null)
                    throw new InvalidOperationException($"Could not find embedded resource: {resourceName}");
                using (var reader = new System.Xml.XmlTextReader(s))
                {
                    var highlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.
                        HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    
                    HighlightingManager.Instance.RegisterHighlighting(highlightingName, new string[] { ".cool" }, highlighting);
                    return highlighting;
                }
            }
        }

        public string GetIconPath(string iconPath)
        {
            if (CurrentTheme == Theme.Dark)
            {
                string darkIconPath = Path.Combine(Path.GetDirectoryName(iconPath) ?? "", 
                    Path.GetFileNameWithoutExtension(iconPath) + ".dark" + Path.GetExtension(iconPath));
                
                if (File.Exists(darkIconPath))
                {
                    return darkIconPath;
                }
            }
            return iconPath;
        }

        public string GetIconResourceName(string resourceName)
        {
            if (CurrentTheme == Theme.Dark)
            {
                string withoutExtension = resourceName.Substring(0, resourceName.LastIndexOf('.'));
                string extension = resourceName.Substring(resourceName.LastIndexOf('.'));
                string darkResourceName = withoutExtension + ".dark" + extension;
                
                return darkResourceName;
            }
            return resourceName;
        }
    }
}
