using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using System.IO;
using System.Reflection;

var xaml = File.ReadAllText("death-client/ui-layout-format.axaml.runtime");
try {
    var root = (Control)AvaloniaRuntimeXamlLoader.Load(xaml, Assembly.Load("FugoLauncher"));
    Console.WriteLine("Parsed OK");
} catch (Exception e) {
    Console.WriteLine(e);
}
