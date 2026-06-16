# SV Injector N5.43.5.1 Build Fix

Fixes converter namespace ambiguity where `Binding.DoNothing` could be resolved against the project namespace instead of WPF's `System.Windows.Data.Binding`.

Changed converter `ConvertBack` methods to return:

```csharp
global::System.Windows.Data.Binding.DoNothing
```

Affected files:

- `apps/AR.Iec61850.SvPublisher/Converters/KindToVisibilityConverter.cs`
- `apps/AR.Iec61850.SvPublisher/Converters/SignalBrushConverter.cs`
