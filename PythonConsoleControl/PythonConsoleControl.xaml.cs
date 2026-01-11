using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;


namespace PythonConsoleControl
{
    /// <summary>
    /// Interaction logic for PythonConsoleControl.xaml
    /// </summary>
    public partial class IronPythonConsoleControl : UserControl
    {
        private const string LightHighlightingName = "Python Console Highlighting";
        private const string DarkHighlightingName = "Python Console Dark Highlighting";
        private const string LightHighlightingResource = "PythonConsoleControl.Resources.Python.xshd";
        private const string DarkHighlightingResource = "PythonConsoleControl.Resources.Python-Dark.xshd";

        private readonly PythonConsolePad _pad;

        /// <summary>
        /// Perform the action on an already instantiated PythonConsoleHost.
        /// </summary>
        public void WithConsoleHost(Action<PythonConsoleHost> action)
        {
            _pad.Host.WhenConsoleCreated(action);
        }

        public IronPythonConsoleControl()
        {
            InitializeComponent();
            _pad = new PythonConsolePad();
            Grid.Children.Add(_pad.Control);
            ApplyTheme(false);
        }

        public void ApplyTheme(bool useDarkTheme)
        {
            ApplyThemeResources();
            var highlightingDefinition = GetHighlightingDefinition(
                useDarkTheme ? DarkHighlightingResource : LightHighlightingResource,
                useDarkTheme ? DarkHighlightingName : LightHighlightingName);
            ApplyHighlighting(highlightingDefinition);
        }

        private void ApplyThemeResources()
        {
            TextEditor editor = _pad.Control;
            Brush backgroundBrush = TryFindResource("ThemeConsoleBackground") as Brush;
            Brush foregroundBrush = TryFindResource("ThemeConsoleForeground") as Brush;

            if (backgroundBrush != null)
            {
                editor.Background = backgroundBrush;
            }

            if (foregroundBrush != null)
            {
                editor.Foreground = foregroundBrush;
            }
        }

        private static IHighlightingDefinition GetHighlightingDefinition(string resourceName, string highlightingName)
        {
            IHighlightingDefinition existingHighlighting = HighlightingManager.Instance.GetDefinition(highlightingName);
            if (existingHighlighting != null)
            {
                return existingHighlighting;
            }

            using (Stream resourceStream = typeof(IronPythonConsoleControl).Assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                {
                    throw new InvalidOperationException($"Could not find embedded resource: {resourceName}");
                }

                using (XmlReader xmlReader = new XmlTextReader(resourceStream))
                {
                    var highlightingDefinition = ICSharpCode.AvalonEdit.Highlighting.Xshd.
                        HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
                    HighlightingManager.Instance.RegisterHighlighting(highlightingName, new string[] { ".py" }, highlightingDefinition);
                    return highlightingDefinition;
                }
            }
        }

        private void ApplyHighlighting(IHighlightingDefinition highlightingDefinition)
        {
            TextEditor editor = _pad.Control;
            editor.SyntaxHighlighting = highlightingDefinition;

            IList<IVisualLineTransformer> lineTransformers = editor.TextArea.TextView.LineTransformers;
            for (int transformerIndex = 0; transformerIndex < lineTransformers.Count; ++transformerIndex)
            {
                if (lineTransformers[transformerIndex] is HighlightingColorizer)
                {
                    lineTransformers[transformerIndex] = new PythonConsoleHighlightingColorizer(highlightingDefinition, editor.Document);
                    return;
                }
            }

            lineTransformers.Add(new PythonConsoleHighlightingColorizer(highlightingDefinition, editor.Document));
        }

        public PythonConsolePad Pad
        {
            get { return _pad; }
        }
    }
}
