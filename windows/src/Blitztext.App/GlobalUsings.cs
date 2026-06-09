// WPF and WinForms coexist in this app: WinForms is used ONLY for the tray NotifyIcon
// (always via the `WinForms.` prefix in TrayIconController). Because both UI stacks are
// implicitly imported, simple type names like Button/Application/Clipboard are ambiguous.
// These aliases make every bare name in the app resolve to its WPF counterpart.

global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using Clipboard = System.Windows.Clipboard;
global using Brush = System.Windows.Media.Brush;
global using Button = System.Windows.Controls.Button;
global using TextBox = System.Windows.Controls.TextBox;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ComboBox = System.Windows.Controls.ComboBox;
global using ProgressBar = System.Windows.Controls.ProgressBar;
global using Label = System.Windows.Controls.Label;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
global using Orientation = System.Windows.Controls.Orientation;
