using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using RevitPythonShell.Helpers;

namespace RevitPythonShell.Views
{
    /// <summary>
    /// Interaction logic for IronPythonConsole.xaml
    /// </summary>
    public partial class IronPythonConsole : Window
    {
        private ConsoleOptions consoleOptionsProvider;

        // this is the name of the file currently being edited in the pad
        private string currentFileName;

        public IronPythonConsole()
        {
            Initialized += new EventHandler(MainWindow_Initialized);

            InitializeComponent();

            var settings = App.GetSettings();
            ThemeManager.Instance.LoadThemeFromSettings(settings);
            ThemeManager.Instance.ApplyRevitThemePreference();
            ApplyTheme();
            ThemeManager.Instance.ThemeChanged += ThemeManager_ThemeChanged;

            textEditor.PreviewKeyDown += new KeyEventHandler(textEditor_PreviewKeyDown);
            consoleOptionsProvider = new ConsoleOptions(consoleControl.Pad);

            StateChanged += IronPythonConsole_StateChanged;

            // get application version and show in title
            Title = String.Format("RevitPythonShell | {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString());
            UpdateMaximizeButton();
        }

        private void MainWindow_Initialized(object sender, EventArgs e)
        {
            //propertyGridComboBox.SelectedIndex = 1;
            textEditor.ShowLineNumbers = true;
        }
        private void newFileClick(object sender, RoutedEventArgs e)
        {
            currentFileName = null;
            textEditor.Text = string.Empty;
        }
        private void openFileClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog() ?? false)
            {
                currentFileName = dlg.FileName;
                textEditor.Load(currentFileName);
                //textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(currentFileName));
            }
        }
        private void saveAsFileClick(object sender, EventArgs e)
        {
            currentFileName = null;
            SaveFile();
        }
        private void saveFileClick(object sender, EventArgs e)
        {
           SaveFile();
        }
        private void SaveFile()
        {
            if (currentFileName == null)
            {
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.Filter = "Save Files (*.py)|*.py";
                dlg.DefaultExt = "py";
                dlg.AddExtension = true;
                if (dlg.ShowDialog() ?? false)
                {
                    currentFileName = dlg.FileName;
                }
                else
                {
                    return;
                }
            }
            textEditor.Save(currentFileName);
        }

        private void runClick(object sender, EventArgs e)
        {
            RunStatements();
        }

        private void toggleThemeClick(object sender, RoutedEventArgs e)
        {
            ThemeManager.Instance.ToggleTheme();
        }
 
        private void textEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5) RunStatements();
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) SaveFile();
            if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) saveAsFileClick(sender, e);
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) openFileClick(sender, e);
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) newFileClick(sender, e);
            if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Control) Close();
            
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void IronPythonConsole_StateChanged(object sender, EventArgs e)
        {
            UpdateMaximizeButton();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void UpdateMaximizeButton()
        {
            if (maximizeButton == null)
            {
                return;
            }

            maximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }


        private void RunStatements()
        {
            string statementsToRun = "";
            if (textEditor.TextArea.Selection.Length > 0)
                statementsToRun = textEditor.TextArea.Selection.GetText();
            else
                statementsToRun = textEditor.TextArea.Document.Text;
            consoleControl.Pad.Console.RunStatements(statementsToRun);
        }

        private void ThemeManager_ThemeChanged(object sender, EventArgs e)
        {
            ApplyTheme();
            SaveThemePreference();
        }

        private void ApplyTheme()
        {
            ThemeManager.Instance.ApplyThemeToWindow(this);
            textEditor.SyntaxHighlighting = ThemeManager.Instance.GetHighlightingDefinition(Assembly.GetExecutingAssembly());
            consoleControl.ApplyTheme(ThemeManager.Instance.CurrentTheme == Theme.Dark);
        }

        private void SaveThemePreference()
        {
            var settings = App.GetSettings();
            ThemeManager.Instance.SaveThemeToSettings(settings);
            settings.Save(GetSettingsFilePath());
        }

        private static string GetSettingsFilePath()
        {
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            return Path.Combine(assemblyFolder, "RevitPythonShell.xml");
        }
 
        // Clear the contents on first click inside the editor
        private void textEditor_GotFocus(object sender, RoutedEventArgs e)
        {
            if (this.currentFileName == null)
            {
                TextEditor tb = (TextEditor)sender;
                tb.Text = string.Empty;
                // Remove the handler from the list otherwise this handler will clear
                // editor contents every time the editor gains focus.
                tb.GotFocus -= textEditor_GotFocus;
            }
        }

    }
}