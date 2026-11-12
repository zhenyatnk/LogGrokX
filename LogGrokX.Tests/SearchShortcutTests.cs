#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LogGrokX.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class SearchShortcutTests
{
    [TestMethod]
    public void WindowAndDocumentTemplatesContainSearchShortcuts()
    {
        RunOnSta(() =>
        {
            var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var windowXaml = XDocument.Load(Path.Combine(repositoryRoot, "LogGrokX", "MainWindow.xaml"));
            var gestures = windowXaml.Descendants()
                .Where(e => e.Name.LocalName == "KeyBinding")
                .Select(e => e.Attribute("Gesture")?.Value?.ToUpperInvariant())
                .ToList();
            Assert.IsTrue(gestures.Contains("CTRL+L"));
            Assert.IsTrue(gestures.Contains("ALT+D"));
            Assert.IsTrue(gestures.Contains("ALT+X"));

            AssertTemplateShortcuts("DocumentTemplate.xaml", "DocumentTemplate");
            AssertTemplateShortcuts("MergedViewTemplate.xaml", "MergedViewTemplate");
        });
    }

    [TestMethod]
    public void SearchTemplateEnablesSelectAllOnlyForPrimaryEditor()
    {
        RunOnSta(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/DocumentSearchTemplate.xaml")
            };
            var template = (DataTemplate)resources["SearchParametersTemplate"];
            var content = (DependencyObject)template.LoadContent();

            Assert.AreEqual(1, FindTextBoxes(content).Count(SetFocusBehavior.GetSelectAllOnFocus));
        });
    }

    [TestMethod]
    public void FocusRequestFocusesEditorAndSelectsAllText()
    {
        RunOnSta(() =>
        {
            var textBox = new TextBox { Text = "synthetic query" };
            var window = new Window { Content = textBox, Width = 300, Height = 100 };
            try
            {
                window.Show();
                textBox.SelectionStart = 4;
                textBox.SelectionLength = 2;
                SetFocusBehavior.SetSelectAllOnFocus(textBox, true);
                var request = new SetFocusRequest();
                SetFocusBehavior.SetSetFocusRequest(textBox, request);

                request.Invoke();

                Assert.AreSame(textBox, Keyboard.FocusedElement);
                Assert.AreEqual(0, textBox.SelectionStart);
                Assert.AreEqual(textBox.Text.Length, textBox.SelectionLength);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void AssertTemplateShortcuts(string fileName, string key)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var templateXaml = XDocument.Load(Path.Combine(repositoryRoot, "LogGrokX", "Styles", fileName));
        var gestures = templateXaml.Descendants()
            .Where(e => e.Name.LocalName == "KeyBinding")
            .Select(e => e.Attribute("Gesture")?.Value?.ToUpperInvariant())
            .ToList();
        Assert.IsTrue(gestures.Contains("CTRL+L"), key);
        Assert.IsTrue(gestures.Contains("ALT+D"), key);
        Assert.IsTrue(gestures.Contains("ALT+X"), key);
    }

    private static IEnumerable<TextBox> FindTextBoxes(DependencyObject root)
    {
        if (root is TextBox textBox)
            yield return textBox;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependencyObject)
                foreach (var descendant in FindTextBoxes(dependencyObject))
                    yield return descendant;
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
