param([string]$Name = 'before', [int]$Width = 0, [int]$Height = 0, [string]$Tab, [string]$Button, [string]$SearchText, [switch]$ClearSearch, [switch]$ScrollToEnd, [string]$FieldId, [string]$FieldValue, [double]$ScrollPercent = -1, [string]$PickerId, [string]$PickerValue, [string]$ItemName)
$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path (Split-Path $PSScriptRoot) 'work\desktop-layout'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LayoutWindow {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
'@
$null = [LayoutWindow]::SetProcessDpiAwarenessContext([IntPtr](-4))
$app = Get-Process -Name 'StarfleetLibrary.Uno' | Sort-Object StartTime -Descending | Select-Object -First 1
if (-not $app) { throw 'Starfleet Library has no open window.' }
$processCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition)
$window = $windows | Where-Object { $_.Current.Name -like 'Starfleet Library*' } | Select-Object -First 1
if (-not $window) { throw 'Main Uno window not found.' }
$handle = [IntPtr]$window.Current.NativeWindowHandle
$null = [LayoutWindow]::ShowWindow($handle, 9)
if ($Width -gt 0 -and $Height -gt 0) { $null = [LayoutWindow]::SetWindowPos($handle, [IntPtr]::Zero, 40, 40, $Width, $Height, 4) }
$null = [LayoutWindow]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 700
$root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
if ($Tab) {
    $condition = New-Object System.Windows.Automation.AndCondition ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Tab)), ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem))
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 600
}
if ($SearchText -or $ClearSearch) {
    $condition = New-Object System.Windows.Automation.AndCondition ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)), ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'CatalogSearch'))
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $item.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($SearchText)
    Start-Sleep -Milliseconds 600
}
if ($PickerId) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $PickerId)
    $picker = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $picker) { throw "Picker not found: $PickerId" }
    $picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 300
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $PickerValue)
    $options = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $selected = $false
    foreach ($option in $options) {
        $selection = $null
        if (-not $option.Current.IsOffscreen -and $option.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) {
            $selection.Select()
            $selected = $true
            break
        }
    }
    if (-not $selected) { throw "Picker option not found: $PickerValue" }
    $picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 700
}
if ($FieldId) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $FieldId)
    $candidates = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $set = $false
    foreach ($candidate in $candidates) {
        $valuePattern = $null
        if (-not $candidate.Current.IsOffscreen -and $candidate.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
            $valuePattern.SetValue($FieldValue)
            $set = $true
            break
        }
    }
    if (-not $set) { throw "Editable field not found: $FieldId" }
    Start-Sleep -Milliseconds 300
}
if ($Button) {
    $condition = New-Object System.Windows.Automation.AndCondition ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Button)), ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { throw "Button not found: $Button" }
    $invoke = $null
    if ($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) { $invoke.Invoke() }
    else { $item.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    Start-Sleep -Milliseconds 600
}
if ($ItemName) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $ItemName)
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { throw "Item not found: $ItemName" }
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 600
}
if ($ScrollToEnd -or $ScrollPercent -ge 0) {
    $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $elements) {
        $scrollPattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$scrollPattern) -and $scrollPattern.Current.VerticallyScrollable) {
            $position = if ($ScrollToEnd) { 100 } else { $ScrollPercent }
            $scrollPattern.SetScrollPercent(-1, $position)
            Start-Sleep -Milliseconds 600
        }
    }
}
$rect = New-Object LayoutWindow+Rect
$null = [LayoutWindow]::GetWindowRect($handle, [ref]$rect)
$image = New-Object System.Drawing.Bitmap ($rect.Right-$rect.Left), ($rect.Bottom-$rect.Top)
$graphics = [System.Drawing.Graphics]::FromImage($image)
try {
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $image.Size)
    $image.Save((Join-Path $outputDirectory "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $image.Dispose() }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
$nodes = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$items = foreach ($node in $nodes) {
    $value = $node.Current
    if (-not $value.IsOffscreen -and $value.BoundingRectangle.Width -gt 0) {
        $scrollPattern = $null
        $scrollInfo = $null
        if ($node.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$scrollPattern)) {
            $scrollInfo = @{Scrollable=$scrollPattern.Current.VerticallyScrollable;Position=$scrollPattern.Current.VerticalScrollPercent;View=$scrollPattern.Current.VerticalViewSize}
        }
        [pscustomobject]@{Name=$value.Name;Id=$value.AutomationId;Type=$value.ControlType.ProgrammaticName;Class=$value.ClassName;Bounds=$value.BoundingRectangle.ToString();Scroll=$scrollInfo}
    }
}
$items | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputDirectory "$Name.json") -Encoding UTF8
[pscustomobject]@{Screenshot=(Join-Path $outputDirectory "$Name.png");Tree=(Join-Path $outputDirectory "$Name.json");Window=$app.MainWindowTitle;Controls=$items.Count} | ConvertTo-Json -Compress


