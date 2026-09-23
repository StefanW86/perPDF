using System.Resources;

// Durch <UICulture>de-DE</UICulture> liegen alle XAML-Ressourcen in der Satellitenassembly
// de-DE\perPDF.resources.dll. Ohne dieses Attribut sucht WPF auf einem nicht-deutschen
// Windows in der Hauptassembly und stürzt beim Start ab („Cannot locate resource 'app.xaml'").
[assembly: NeutralResourcesLanguage("de-DE", UltimateResourceFallbackLocation.Satellite)]
