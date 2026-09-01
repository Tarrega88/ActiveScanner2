using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ActiveScanner.Models;
using ActiveScanner.ViewModels;
using ActiveScanner.Views;
using MaterialDesignThemes.Wpf;

namespace ActiveScanner
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;
        
        // Drag selection state
        private bool _isDragSelecting;
        private Point _dragStartPoint;
        private int _dragStartRowIndex = -1;
        
        // Shift-click range selection anchor
        private int _shiftClickAnchorIndex = -1;
        
        // Selection batching - suppress handling during bulk selection operations
        private bool _isBatchSelecting;
        
        // Right-clicked item for context menu
        private Models.AdObjectInfo? _rightClickedItem;
        
        // Debounce timer for filter-on-input
        private System.Timers.Timer? _filterDebounceTimer;

        // Cell peek (hold-to-preview) state
        private System.Windows.Threading.DispatcherTimer? _cellPeekTimer;
        private Point _cellPeekMousePos;

        // Ensures the column-filter TextBox class handler is registered only once.
        private static bool _filterBoxClassHandlerRegistered;

        public MainWindow()
        {
            RegisterFilterBoxClassHandler();
            InitializeComponent();
        }

        /// <summary>
        /// Works around a WPF quirk where the Vulnerabilities findings grid is first measured
        /// while its tab container is collapsed, so the star-sized "Name" column computes to
        /// width 0 and the columns scrunch to the left until you navigate away and back. When the
        /// grid actually becomes visible (with a real width), force a fresh star-width calculation.
        /// </summary>
        private void VulnFindingsGrid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is DataGrid grid)
                ScheduleVulnColumnFix(grid);
        }

        private void VulnFindingsGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
                ScheduleVulnColumnFix(grid);
        }

        /// <summary>
        /// Re-applies the findings grid's star columns once it has a real width, forcing WPF to
        /// re-divide the available space. Runs at Background priority (after the layout pass) and
        /// retries if the grid isn't measured yet.
        /// </summary>
        private void ScheduleVulnColumnFix(DataGrid grid, int attempt = 0)
        {
            if (!grid.IsVisible || attempt > 10)
                return;

            grid.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!grid.IsVisible)
                    return;

                // Not laid out yet — try again after the next layout pass.
                if (grid.ActualWidth <= 1)
                {
                    ScheduleVulnColumnFix(grid, attempt + 1);
                    return;
                }

                var stars = grid.Columns
                    .Where(c => c.Width.IsStar)
                    .Select(c => (Column: c, Star: c.Width))
                    .ToList();
                if (stars.Count == 0)
                    return;

                // Collapse the star columns, force a layout pass, then restore them so the
                // available width is re-divided against the grid's real (non-zero) width.
                foreach (var s in stars)
                    s.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Pixel);
                grid.UpdateLayout();
                foreach (var s in stars)
                    s.Column.Width = s.Star;
                grid.UpdateLayout();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Registers a one-time class handler that upgrades every column-filter TextBox
        /// (bound to a "Data.Filter*" path) to accept multi-line input and get an
        /// "expand" button that opens the larger modal filter editor.
        /// </summary>
        private static void RegisterFilterBoxClassHandler()
        {
            if (_filterBoxClassHandlerRegistered) return;
            _filterBoxClassHandlerRegistered = true;
            EventManager.RegisterClassHandler(typeof(TextBox), LoadedEvent,
                new RoutedEventHandler(FilterTextBox_Loaded));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore window position/size
            var settings = App.Settings.Current;
            
            if (settings.WindowLeft >= 0 && settings.WindowTop >= 0)
            {
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
            }

            if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
            {
                Width = settings.WindowWidth;
                Height = settings.WindowHeight;
            }

            if (settings.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }

            // Subscribe to SelectAll event from ViewModel
            if (DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.SelectAllRequested += OnSelectAllRequested;
                
                // Subscribe to the initially selected tab's column visibility changes
                if (viewModel.SelectedOutputTab != null)
                {
                    _currentVisibilityTrackedTab = viewModel.SelectedOutputTab;
                    _currentVisibilityTrackedTab.ColumnVisibilityChanged += OnColumnVisibilityChanged;
                }
            }
        }

        private void OnSelectAllRequested()
        {
            // If a TextBox is focused, let it handle Ctrl+A natively (select all text)
            if (Keyboard.FocusedElement is TextBox)
                return;
            
            // DataGrid is inside a DataTemplate, so we need to find it
            var dataGrid = FindVisualChild<DataGrid>(this);
            if (dataGrid != null)
            {
                // Batch select all to avoid performance issues
                _isBatchSelecting = true;
                try
                {
                    dataGrid.SelectAll();
                }
                finally
                {
                    _isBatchSelecting = false;
                    ProcessSelectionChange(dataGrid);
                }
            }
        }

        /// <summary>
        /// Finds the first visual child of a specific type
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Unsubscribe from events
            if (DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.SelectAllRequested -= OnSelectAllRequested;
            }

            // Save window position/size
            var settings = App.Settings.Current;

            if (WindowState == WindowState.Normal)
            {
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
            }

            settings.IsMaximized = WindowState == WindowState.Maximized;

            // Cleanup ViewModel
            ViewModel.Cleanup();
            
            // Force application shutdown to kill any lingering background tasks
            Application.Current.Shutdown();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Views.SettingsDialog();
            dialog.Owner = this;
            dialog.ShowDialog();

            // If settings were completely reset, do a full refresh
            if (dialog.SettingsReset)
            {
                // Apply theme from fresh settings
                App.ApplyTheme(App.Settings.Current.DarkMode);
                
                // Refresh all ViewModel data
                ViewModel.RefreshSavedGroups();
                ViewModel.RefreshAfterSettingsReset();
            }
            // If groups were imported, refresh the ViewModel's collection
            else if (dialog.GroupsImported)
            {
                ViewModel.RefreshSavedGroups();
            }
        }

        private void OpenHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            helpWindow.ShowDialog();
        }

        /// <summary>
        /// Handle text changes in filter textboxes with debouncing
        /// </summary>
        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Cancel any pending filter operation
            _filterDebounceTimer?.Stop();
            _filterDebounceTimer?.Dispose();
            
            // Start a new debounce timer (500ms delay)
            _filterDebounceTimer = new System.Timers.Timer(500);
            _filterDebounceTimer.AutoReset = false;
            _filterDebounceTimer.Elapsed += (s, args) =>
            {
                // Use BeginInvoke with Background priority so input processing takes precedence
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    try
                    {
                        ViewModel.SelectedOutputTab?.ApplyFilterAndSort();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Filter error: {ex.Message}");
                    }
                });
            };
            _filterDebounceTimer.Start();
        }

        /// <summary>
        /// Handle Enter key in filter textboxes to trigger immediate filtering.
        /// Shift+Enter inserts a newline (used as the OR separator) instead of submitting.
        /// </summary>
        private void FilterTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Shift+Enter adds a new line (an additional OR term) - let it through.
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    return;

                // Cancel debounce timer and apply immediately
                _filterDebounceTimer?.Stop();
                
                // Update the binding source (push the text to the ViewModel)
                if (sender is TextBox textBox)
                {
                    var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                }
                
                // Apply the filter immediately
                try
                {
                    ViewModel.SelectedOutputTab?.ApplyFilterAndSort();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Filter error: {ex.Message}");
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// Upgrades a column-filter TextBox once it is loaded: enables multi-line input
        /// (so pasted Excel columns keep their newlines) and injects an "expand" button
        /// that opens the larger modal filter editor. Scoped strictly to boxes whose Text
        /// is bound to a "Data.Filter*" path so unrelated TextBoxes are untouched.
        /// </summary>
        private static void FilterTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            var path = tb.GetBindingExpression(TextBox.TextProperty)?.ParentBinding?.Path?.Path;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Data.Filter", StringComparison.Ordinal))
                return;

            // Allow multi-line content while keeping the compact single-line look.
            tb.AcceptsReturn = true;
            tb.AcceptsTab = false;
            tb.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            tb.VerticalContentAlignment = VerticalAlignment.Center;

            // "Data.FilterName" -> "FilterName"
            var filterProperty = path.Substring("Data.".Length);

            // Walk up to the column-header StackPanel:
            // TextBox -> inner Grid -> filter Grid -> header StackPanel (first child = sort button).
            if (tb.Parent is not Grid innerGrid ||
                innerGrid.Parent is not Grid filterGrid ||
                filterGrid.Parent is not StackPanel headerPanel ||
                headerPanel.Children.Count == 0 ||
                headerPanel.Children[0] is not Button sortButton)
                return;

            var expandButton = new Button
            {
                Style = tb.TryFindResource("MaterialDesignToolButton") as Style,
                Width = 16,
                Height = 16,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
                Tag = FilterExpandTag,
                ToolTip = "Open filter editor (paste a list)",
                Content = new PackIcon { Kind = PackIconKind.OpenInNew, Width = 12, Height = 12 }
            };
            expandButton.Click += (s, args) =>
            {
                var window = Window.GetWindow(tb) as MainWindow;
                window?.OpenFilterEditor(filterProperty);
            };

            // Move the sort button into a two-column Grid so the expand button can sit
            // right-aligned on the same row as the column header text.
            int index = headerPanel.Children.IndexOf(sortButton);
            headerPanel.Children.Remove(sortButton);

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(sortButton, 0);
            Grid.SetColumn(expandButton, 1);
            headerRow.Children.Add(sortButton);
            headerRow.Children.Add(expandButton);
            headerPanel.Children.Insert(index, headerRow);

            // No second button inside the box now, so restore the compact right padding.
            tb.Padding = new Thickness(4, 4, 20, 4);
        }

        private const string FilterExpandTag = "__filterExpand";

        /// <summary>
        /// Opens the modal filter editor for the given tab filter property, live-bound to
        /// the current tab so edits are reflected in the inline box and the grid.
        /// </summary>
        private void OpenFilterEditor(string filterProperty)
        {
            var tab = ViewModel.SelectedOutputTab;
            if (tab == null)
                return;

            var window = new FilterExpandWindow(tab, filterProperty) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>
        /// Handle ComboBox selection changed for filter dropdowns
        /// </summary>
        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Apply the filter when ComboBox selection changes
            try
            {
                if (sender is System.Windows.Controls.ComboBox comboBox && ViewModel.SelectedOutputTab != null)
                {
                    // Directly set the FilterEnabled based on selected index
                    // Index 0 = All (null), Index 1 = Yes (true), Index 2 = No (false)
                    ViewModel.SelectedOutputTab.FilterEnabled = comboBox.SelectedIndex switch
                    {
                        1 => true,
                        2 => false,
                        _ => null
                    };
                }
                ViewModel.SelectedOutputTab?.ApplyFilterAndSort();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Filter error on combobox change: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle Enabled filter ComboBox selection changed
        /// </summary>
        private void EnabledFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"EnabledFilter_SelectionChanged fired!");
            try
            {
                if (sender is System.Windows.Controls.ComboBox comboBox)
                {
                    System.Diagnostics.Debug.WriteLine($"ComboBox SelectedIndex: {comboBox.SelectedIndex}");
                    if (ViewModel?.SelectedOutputTab != null)
                    {
                        // Index 0 = All (null), Index 1 = Yes (true), Index 2 = No (false)
                        ViewModel.SelectedOutputTab.FilterEnabled = comboBox.SelectedIndex switch
                        {
                            1 => true,
                            2 => false,
                            _ => null
                        };
                        System.Diagnostics.Debug.WriteLine($"Set FilterEnabled to: {ViewModel.SelectedOutputTab.FilterEnabled}");
                        ViewModel.SelectedOutputTab.ApplyFilterAndSort();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnabledFilter error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle clear button click for individual filter inputs
        /// </summary>
        private void ClearSingleFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filterName && ViewModel.SelectedOutputTab != null)
            {
                var tab = ViewModel.SelectedOutputTab;
                switch (filterName)
                {
                    case "FilterName": tab.FilterName = string.Empty; break;
                    case "FilterDescription": tab.FilterDescription = string.Empty; break;
                    case "FilterSamAccountName": tab.FilterSamAccountName = string.Empty; break;
                    case "FilterEmail": tab.FilterEmail = string.Empty; break;
                    case "FilterTitle": tab.FilterTitle = string.Empty; break;
                    case "FilterDepartment": tab.FilterDepartment = string.Empty; break;
                    case "FilterManager": tab.FilterManager = string.Empty; break;
                    case "FilterSource": tab.FilterSource = string.Empty; break;
                    case "FilterLastUser": tab.FilterLastUser = string.Empty; break;
                    case "FilterComputerHistory": tab.FilterComputerHistory = string.Empty; break;
                    case "FilterSnowId": tab.FilterSnowId = string.Empty; break;
                    case "FilterIpAddress": tab.FilterIpAddress = string.Empty; break;
                    case "FilterShareName": tab.FilterShareName = string.Empty; break;
                    case "FilterDriverName": tab.FilterDriverName = string.Empty; break;
                    case "FilterUncName": tab.FilterUncName = string.Empty; break;
                    case "FilterLocation": tab.FilterLocation = string.Empty; break;
                    case "FilterLastActivityDays": tab.FilterLastActivityDays = null; break;
                }
                // Cancel debounce timer AFTER property change (which triggers TextChanged and starts a new timer)
                _filterDebounceTimer?.Stop();
                tab.ApplyFilterAndSort();
            }
        }

        /// <summary>
        /// Force column width recalculation when DataGrid is first loaded.
        /// This fixes an issue where star-sized columns are too thin on first tab display.
        /// </summary>
        private void ResultsDataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                // Use multiple deferred invocations to ensure layout is fully complete
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
                {
                    RefreshDataGridLayout(dataGrid);
                }));
            }
        }

        private void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // If we're in a batch selection operation, defer processing until complete
            if (_isBatchSelecting)
            {
                return;
            }
            
            ProcessSelectionChange(sender as DataGrid);
        }
        
        private void ProcessSelectionChange(DataGrid? dataGrid)
        {
            if (dataGrid == null) return;

            // Use batch mode to avoid firing notifications for each add/remove
            ViewModel.BeginBatchSelection();
            try
            {
                ViewModel.SelectedComputers.Clear();
                ViewModel.SelectedAdObjects.Clear();

                foreach (var item in dataGrid.SelectedItems)
                {
                    if (item is AdObjectInfo adObject)
                    {
                        ViewModel.SelectedAdObjects.Add(adObject);
                        // For network operations, also add computers to SelectedComputers
                        if (adObject.ObjectType == AdObjectType.Computer)
                        {
                            ViewModel.SelectedComputers.Add(adObject);
                        }
                    }
                }

                // Also set single selected computer for single-target operations
                if (dataGrid.SelectedItem is AdObjectInfo selectedAdObject && selectedAdObject.ObjectType == AdObjectType.Computer)
                {
                    ViewModel.SelectedComputer = selectedAdObject;
                }
                else
                {
                    ViewModel.SelectedComputer = null;
                }

                // Update selected count for status bar display
                ViewModel.SelectedCount = dataGrid.SelectedItems.Count;
            }
            finally
            {
                ViewModel.EndBatchSelection();
            }
        }

        #region DataGrid Drag Selection

        private void ResultsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid dataGrid) return;
            
            // Check if the click is on an interactive control - if so, let it handle the event
            if (e.OriginalSource is DependencyObject source)
            {
                var button = FindVisualParent<Button>(source);
                var comboBox = FindVisualParent<System.Windows.Controls.ComboBox>(source);
                var comboBoxItem = FindVisualParent<ComboBoxItem>(source);
                var textBox = FindVisualParent<TextBox>(source);
                var popup = FindVisualParent<System.Windows.Controls.Primitives.Popup>(source);
                if (button != null || comboBox != null || comboBoxItem != null || textBox != null || popup != null)
                {
                    // Don't interfere with interactive control clicks
                    return;
                }
            }
            
            // Get the row under the mouse
            var row = GetRowAtPoint(dataGrid, e.GetPosition(dataGrid));
            if (row != null)
            {
                _dragStartPoint = e.GetPosition(dataGrid);
                _dragStartRowIndex = row.GetIndex();

                // Start cell peek timer (hold-to-preview)
                _cellPeekMousePos = PointToScreen(e.GetPosition(this));
                StartCellPeekTimer(dataGrid, e);
                
                var item = dataGrid.Items[_dragStartRowIndex];
                
                // If holding Ctrl, toggle selection
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    if (dataGrid.SelectedItems.Contains(item))
                    {
                        dataGrid.SelectedItems.Remove(item);
                    }
                    else
                    {
                        dataGrid.SelectedItems.Add(item);
                        _shiftClickAnchorIndex = _dragStartRowIndex; // Update anchor to last added
                    }
                }
                // If holding Shift, extend selection from anchor to clicked row
                else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    // If we have an anchor, select range from anchor to current
                    if (_shiftClickAnchorIndex >= 0 && _shiftClickAnchorIndex < dataGrid.Items.Count)
                    {
                        var minIndex = Math.Min(_shiftClickAnchorIndex, _dragStartRowIndex);
                        var maxIndex = Math.Max(_shiftClickAnchorIndex, _dragStartRowIndex);
                        
                        // Batch the selection to avoid O(n²) performance
                        _isBatchSelecting = true;
                        try
                        {
                            dataGrid.SelectedItems.Clear();
                            for (int i = minIndex; i <= maxIndex; i++)
                            {
                                if (i >= 0 && i < dataGrid.Items.Count)
                                {
                                    dataGrid.SelectedItems.Add(dataGrid.Items[i]);
                                }
                            }
                        }
                        finally
                        {
                            _isBatchSelecting = false;
                            // Process the selection once at the end
                            ProcessSelectionChange(dataGrid);
                        }
                        // Don't update anchor on shift-click - keep it at the original position
                    }
                    else
                    {
                        // No anchor set, just select the clicked item and set it as anchor
                        dataGrid.SelectedItems.Clear();
                        dataGrid.SelectedItems.Add(item);
                        _shiftClickAnchorIndex = _dragStartRowIndex;
                    }
                    e.Handled = true; // Prevent default shift behavior
                }
                // Normal click - clear selection and select clicked row
                else
                {
                    dataGrid.SelectedItems.Clear();
                    dataGrid.SelectedItems.Add(item);
                    _shiftClickAnchorIndex = _dragStartRowIndex; // Set new anchor on normal click
                }
                
                _isDragSelecting = true;
                dataGrid.CaptureMouse();
            }
        }

        private void ResultsDataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CancelCellPeek();
            if (sender is DataGrid dataGrid && _isDragSelecting)
            {
                _isDragSelecting = false;
                _dragStartRowIndex = -1;
                dataGrid.ReleaseMouseCapture();
            }
        }

        private void ResultsDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragSelecting || sender is not DataGrid dataGrid) return;
            if (_dragStartRowIndex < 0) return;

            var currentPoint = e.GetPosition(dataGrid);
            
            // Auto-scroll when dragging near edges
            AutoScrollDataGrid(dataGrid, currentPoint);
            
            // Check if mouse has moved enough to consider it a drag
            if (Math.Abs(currentPoint.Y - _dragStartPoint.Y) < 5) return;

            // Mouse moved significantly - cancel cell peek
            CancelCellPeek();

            var currentRow = GetRowAtPoint(dataGrid, currentPoint);
            
            // If we're outside the visible area, estimate the row index based on scroll position
            int currentRowIndex;
            if (currentRow != null)
            {
                currentRowIndex = currentRow.GetIndex();
            }
            else
            {
                // Estimate row index when outside visible area
                currentRowIndex = EstimateRowIndexAtPoint(dataGrid, currentPoint);
                if (currentRowIndex < 0) return;
            }
            
            // Select all rows between start and current
            var minIndex = Math.Min(_dragStartRowIndex, currentRowIndex);
            var maxIndex = Math.Max(_dragStartRowIndex, currentRowIndex);

            // Clamp to valid range
            minIndex = Math.Max(0, minIndex);
            maxIndex = Math.Min(dataGrid.Items.Count - 1, maxIndex);

            // Batch the selection to avoid O(n²) performance
            _isBatchSelecting = true;
            try
            {
                // If holding Ctrl or Shift, add to existing selection; otherwise replace
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                {
                    dataGrid.SelectedItems.Clear();
                }

                for (int i = minIndex; i <= maxIndex; i++)
                {
                    var item = dataGrid.Items[i];
                    if (!dataGrid.SelectedItems.Contains(item))
                    {
                        dataGrid.SelectedItems.Add(item);
                    }
                }
            }
            finally
            {
                _isBatchSelecting = false;
                // Process once at the end
                ProcessSelectionChange(dataGrid);
            }
        }

        private void ResultsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not DataGrid dataGrid) return;

            // Handle Ctrl+A for select all
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // If focus is in a TextBox (e.g., filter fields), let the default behavior select text
                if (Keyboard.FocusedElement is TextBox)
                {
                    return; // Don't handle - let TextBox do its default Ctrl+A (select all text)
                }
                
                // Batch select all to avoid performance issues
                _isBatchSelecting = true;
                try
                {
                    dataGrid.SelectAll();
                }
                finally
                {
                    _isBatchSelecting = false;
                    ProcessSelectionChange(dataGrid);
                }
                e.Handled = true;
            }
        }

        private void AutoScrollDataGrid(DataGrid dataGrid, Point mousePosition)
        {
            var scrollViewer = GetScrollViewer(dataGrid);
            if (scrollViewer == null) return;

            const double scrollThreshold = 30; // pixels from edge to trigger scroll
            const double scrollSpeed = 5; // pixels per tick

            if (mousePosition.Y < scrollThreshold)
            {
                // Scroll up
                var scrollAmount = (scrollThreshold - mousePosition.Y) / scrollThreshold * scrollSpeed;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollAmount);
            }
            else if (mousePosition.Y > dataGrid.ActualHeight - scrollThreshold)
            {
                // Scroll down
                var distanceFromBottom = mousePosition.Y - (dataGrid.ActualHeight - scrollThreshold);
                var scrollAmount = distanceFromBottom / scrollThreshold * scrollSpeed;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollAmount);
            }
        }

        private int EstimateRowIndexAtPoint(DataGrid dataGrid, Point point)
        {
            var scrollViewer = GetScrollViewer(dataGrid);
            if (scrollViewer == null) return -1;

            // Estimate row height (typically around 25-30 pixels)
            const double estimatedRowHeight = 28;
            
            if (point.Y < 0)
            {
                // Above the visible area - estimate based on scroll position
                var rowsAbove = (int)(scrollViewer.VerticalOffset / estimatedRowHeight);
                var additionalRows = (int)(-point.Y / estimatedRowHeight);
                return Math.Max(0, rowsAbove - additionalRows);
            }
            else if (point.Y > dataGrid.ActualHeight)
            {
                // Below the visible area
                var rowsAbove = (int)(scrollViewer.VerticalOffset / estimatedRowHeight);
                var visibleRows = (int)(dataGrid.ActualHeight / estimatedRowHeight);
                var additionalRows = (int)((point.Y - dataGrid.ActualHeight) / estimatedRowHeight);
                return Math.Min(dataGrid.Items.Count - 1, rowsAbove + visibleRows + additionalRows);
            }
            
            return -1;
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject obj)
        {
            if (obj is ScrollViewer sv) return sv;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private static DataGridRow? GetRowAtPoint(DataGrid dataGrid, Point point)
        {
            var element = dataGrid.InputHitTest(point) as DependencyObject;
            while (element != null && element is not DataGridRow)
            {
                element = VisualTreeHelper.GetParent(element);
            }
            return element as DataGridRow;
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typedParent)
                {
                    return typedParent;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        #endregion

        #region Cell Peek (Hold-to-Preview)

        private void StartCellPeekTimer(DataGrid dataGrid, MouseButtonEventArgs e)
        {
            CancelCellPeek();
            
            // Capture the original source for hit-testing later
            var originalSource = e.OriginalSource as DependencyObject;
            
            _cellPeekTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _cellPeekTimer.Tick += (s, args) =>
            {
                _cellPeekTimer?.Stop();
                ShowCellPeek(dataGrid, originalSource);
            };
            _cellPeekTimer.Start();
        }

        private void ShowCellPeek(DataGrid dataGrid, DependencyObject? hitSource)
        {
            if (hitSource == null) return;

            // Walk up to find the DataGridCell
            var cell = FindVisualParent<DataGridCell>(hitSource);
            if (cell == null) return;

            // Extract the displayed text from the cell
            var text = GetCellText(cell);
            if (string.IsNullOrWhiteSpace(text)) return;

            CellPeekText.Text = text;
            CellPeekPopup.HorizontalOffset = _cellPeekMousePos.X;
            CellPeekPopup.VerticalOffset = _cellPeekMousePos.Y - 40;
            CellPeekPopup.IsOpen = true;
        }

        private void CancelCellPeek()
        {
            _cellPeekTimer?.Stop();
            _cellPeekTimer = null;
            CellPeekPopup.IsOpen = false;
        }

        private static string GetCellText(DataGridCell cell)
        {
            // Look for TextBlock content inside the cell
            var textBlock = FindVisualChild<TextBlock>(cell);
            if (textBlock != null)
                return textBlock.Text;

            // Fallback: check for ContentPresenter with string content
            var presenter = FindVisualChild<ContentPresenter>(cell);
            if (presenter?.Content is string str)
                return str;

            return string.Empty;
        }

        #endregion

        #region Context Menu Handlers

        /// <summary>
        /// Handles context menu opening to determine the right-clicked item and update menu headers
        /// </summary>
        private void ResultsDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not DataGrid dataGrid) return;
            var contextMenu = dataGrid.ContextMenu;
            if (contextMenu == null) return;
            
            // Get the item under the mouse
            var position = Mouse.GetPosition(dataGrid);
            var hitTestResult = VisualTreeHelper.HitTest(dataGrid, position);
            
            _rightClickedItem = null;
            
            if (hitTestResult?.VisualHit != null)
            {
                // Walk up the visual tree to find the DataGridRow
                var row = FindVisualParent<DataGridRow>(hitTestResult.VisualHit);
                if (row?.Item is Models.AdObjectInfo adObj)
                {
                    _rightClickedItem = adObj;
                }
            }
            
            // Update menu item headers
            var itemName = _rightClickedItem?.Name ?? "Selected";
            
            // Get selected items directly from the DataGrid
            var selectedItems = dataGrid.SelectedItems.OfType<Models.AdObjectInfo>().ToList();
            
            // Build selection count text
            string selectionText;
            if (selectedItems.Count == 0)
            {
                selectionText = "No selection";
            }
            else if (selectedItems.Count == 1)
            {
                var item = selectedItems.First();
                var typeName = item.ObjectType switch
                {
                    Models.AdObjectType.Computer => "computer",
                    Models.AdObjectType.User => "user",
                    Models.AdObjectType.Printer => "printer",
                    _ => "item"
                };
                selectionText = $"1 {typeName} selected";
            }
            else
            {
                // Count by type
                var computers = selectedItems.Count(i => i.ObjectType == Models.AdObjectType.Computer);
                var users = selectedItems.Count(i => i.ObjectType == Models.AdObjectType.User);
                var printers = selectedItems.Count(i => i.ObjectType == Models.AdObjectType.Printer);
                
                var parts = new List<string>();
                if (computers > 0) parts.Add($"{computers} computer{(computers > 1 ? "s" : "")}");
                if (users > 0) parts.Add($"{users} user{(users > 1 ? "s" : "")}");
                if (printers > 0) parts.Add($"{printers} printer{(printers > 1 ? "s" : "")}");
                
                selectionText = parts.Count > 0 
                    ? string.Join(", ", parts) + " selected"
                    : $"{selectedItems.Count} items selected";
            }
            
            foreach (var item in contextMenu.Items.OfType<MenuItem>())
            {
                if (item.Name == "SelectionCountMenuItem")
                {
                    item.Header = selectionText;
                }
                else if (item.Name == "ViewPropertiesMenuItem")
                {
                    item.Header = $"View Properties ({itemName})";
                }
                else if (item.Name == "RdpMenuItem")
                {
                    item.Header = $"Remote Desktop ({itemName})";
                }
                else if (item.Name == "CopyMenuItem")
                {
                    PopulateCopySubmenu(item, selectedItems, dataGrid);
                }
                else if (item.Name == "EditAdMenuItem")
                {
                    // Only show Edit menu for single selection
                    item.Visibility = selectedItems.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
                    
                    // Update Enable/Disable menu item based on the right-clicked item
                    if (_rightClickedItem != null && selectedItems.Count == 1)
                    {
                        foreach (var subItem in item.Items.OfType<MenuItem>())
                        {
                            if (subItem.Name == "EnableDisableMenuItem")
                            {
                                // Only show for computers and users, not printers
                                var objType = _rightClickedItem.ObjectType;
                                if (objType == Models.AdObjectType.Printer)
                                {
                                    subItem.Visibility = Visibility.Collapsed;
                                }
                                else
                                {
                                    subItem.Visibility = Visibility.Visible;
                                    
                                    // Update header and icon based on current state
                                    if (_rightClickedItem.IsEnabled)
                                    {
                                        subItem.Header = "Disable";
                                        if (subItem.Icon is MaterialDesignThemes.Wpf.PackIcon icon)
                                        {
                                            icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.AccountCancel;
                                        }
                                    }
                                    else
                                    {
                                        subItem.Header = "Enable";
                                        if (subItem.Icon is MaterialDesignThemes.Wpf.PackIcon icon)
                                        {
                                            icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.AccountCheck;
                                        }
                                    }
                                }
                            }
                            else if (subItem.Name == "DeleteComputerMenuItem")
                            {
                                // Only show Delete for computers (not users or printers)
                                subItem.Visibility = _rightClickedItem.ObjectType == Models.AdObjectType.Computer 
                                    ? Visibility.Visible 
                                    : Visibility.Collapsed;
                            }
                        }
                        
                        // Also handle the separator before Delete
                        foreach (var subItem in item.Items.OfType<Separator>())
                        {
                            if (subItem.Name == "DeleteSeparator")
                            {
                                subItem.Visibility = _rightClickedItem.ObjectType == Models.AdObjectType.Computer 
                                    ? Visibility.Visible 
                                    : Visibility.Collapsed;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Populates the Copy submenu with row and column-specific options
        /// </summary>
        private void PopulateCopySubmenu(MenuItem copyMenuItem, List<AdObjectInfo> selectedItems, DataGrid dataGrid)
        {
            copyMenuItem.Items.Clear();

            // Copy Row(s) option at the top
            var copyRowItem = new MenuItem
            {
                Header = selectedItems.Count > 1 ? "Copy Rows" : "Copy Row"
            };
            copyRowItem.Click += CopyRow_Click;
            copyMenuItem.Items.Add(copyRowItem);

            copyMenuItem.Items.Add(new Separator());

            // Determine which columns to show based on current tab type
            var columns = GetCopyableColumns(dataGrid);

            foreach (var (columnName, propertyName) in columns)
            {
                var menuItem = new MenuItem
                {
                    Header = columnName,
                    Tag = propertyName  // Store property name for lookup
                };
                menuItem.Click += CopyColumnValue_Click;
                copyMenuItem.Items.Add(menuItem);

                // Right after the "Last User" entry, add a "Last User + Email" variant.
                // Format: "{LastUser} - {email}", or just "{LastUser}" when no email is on file.
                if (propertyName == "LastUser")
                {
                    var withEmailItem = new MenuItem
                    {
                        Header = "Last User + Email",
                        Tag = "LastUserWithEmail"
                    };
                    withEmailItem.Click += CopyColumnValue_Click;
                    copyMenuItem.Items.Add(withEmailItem);
                }
            }
        }

        // Display name mapping for column SortMemberPath values
        private static readonly Dictionary<string, string> _columnDisplayNames = new()
        {
            { "Name", "Name" },
            { "IpAddress", "IP Address" },
            { "ShareName", "Share" },
            { "Description", "Description" },
            { "IsEnabled", "Enabled" },
            { "DistinguishedName", "Source" },
            { "LastUserDate", "Last User" },  // Maps to LastUser property for actual value
            { "LastActivity", "Last Activity" },
            { "LastLogon", "AD Logon" },
            { "SamAccountName", "SAM" },
            { "Mail", "Email" },
            { "ComputerHistoryDisplay", "Computer History" },
            { "Title", "Title" },
            { "Department", "Dept" },
            { "Manager", "Manager" },
            { "DriverName", "Driver/Model" },
            { "UNCName", "UNC Path" },
            { "Location", "Location" }
        };

        // Property name mapping for columns where SortMemberPath differs from the actual property to copy
        private static readonly Dictionary<string, string> _sortMemberToPropertyName = new()
        {
            { "LastUserDate", "LastUser" }  // Last User column sorts by activity time but copies the username
        };

        /// <summary>
        /// Gets the list of columns that can be copied for the current tab type.
        /// Reads directly from the DataGrid to ensure column order matches visual display.
        /// </summary>
        private List<(string DisplayName, string PropertyName)> GetCopyableColumns(DataGrid? dataGrid)
        {
            var columns = new List<(string DisplayName, string PropertyName)>();
            
            if (dataGrid == null) return columns;
            
            // Read columns from the DataGrid in display order
            var orderedColumns = dataGrid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();
            
            foreach (var column in orderedColumns)
            {
                var sortMemberPath = column.SortMemberPath;
                if (string.IsNullOrEmpty(sortMemberPath)) continue;
                
                // Get display name from mapping, fallback to SortMemberPath
                var displayName = _columnDisplayNames.TryGetValue(sortMemberPath, out var name) 
                    ? name 
                    : sortMemberPath;
                
                // Get the actual property name (may differ from SortMemberPath)
                var propertyName = _sortMemberToPropertyName.TryGetValue(sortMemberPath, out var mapped)
                    ? mapped
                    : sortMemberPath;
                
                columns.Add((displayName, propertyName));
            }
            
            return columns;
        }
        
        /// <summary>
        /// Gets the DataGrid from a context menu item
        /// </summary>
        private DataGrid? GetDataGridFromMenuItem(object sender)
        {
            if (sender is MenuItem menuItem)
            {
                // Walk up to find the ContextMenu
                DependencyObject? parent = menuItem.Parent;
                while (parent != null)
                {
                    if (parent is ContextMenu contextMenu)
                    {
                        if (contextMenu.PlacementTarget is DataGrid dataGrid)
                            return dataGrid;
                        break;
                    }
                    
                    if (parent is MenuItem parentMenuItem)
                        parent = parentMenuItem.Parent;
                    else
                        break;
                }
            }
            return null;
        }

        /// <summary>
        /// Copies entire row(s) as tab-separated values with headers, plus HTML table for rich paste in Teams/Outlook
        /// </summary>
        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ViewModel.SelectedAdObjects.ToList();
            if (selectedItems.Count == 0) return;

            var dataGrid = GetDataGridFromMenuItem(sender);
            var columns = GetCopyableColumns(dataGrid);
            
            if (columns.Count == 0) return;
            
            // Build all rows first to calculate max widths
            var headers = columns.Select(c => c.DisplayName).ToList();
            var dataRows = new List<List<string>>();
            foreach (var item in selectedItems)
            {
                var values = columns
                    .Select(c => GetPropertyValue(item, c.PropertyName) ?? "")
                    .ToList();
                dataRows.Add(values);
            }
            
            // Calculate max width for each column (including header)
            var columnWidths = new int[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                columnWidths[i] = headers[i].Length;
                foreach (var row in dataRows)
                {
                    if (row[i].Length > columnWidths[i])
                        columnWidths[i] = row[i].Length;
                }
            }
            
            // Build plain text output with padded columns
            var lines = new List<string>();
            
            // Add header row with padding
            var paddedHeaders = headers.Select((h, i) => h.PadRight(columnWidths[i])).ToList();
            lines.Add(string.Join("\t", paddedHeaders));
            
            // Add data rows with padding
            foreach (var row in dataRows)
            {
                var paddedValues = row.Select((v, i) => v.PadRight(columnWidths[i])).ToList();
                lines.Add(string.Join("\t", paddedValues));
            }

            if (lines.Count > 0)
            {
                var plainText = string.Join(Environment.NewLine, lines);
                var htmlTable = BuildHtmlTable(headers, dataRows);
                
                // Set clipboard with both plain text and HTML formats
                var dataObject = new DataObject();
                dataObject.SetText(plainText);
                dataObject.SetData(DataFormats.Html, htmlTable);
                Clipboard.SetDataObject(dataObject, true);
            }
        }
        
        /// <summary>
        /// Builds an HTML table string in clipboard HTML format for rich paste in Teams/Outlook
        /// </summary>
        private static string BuildHtmlTable(List<string> headers, List<List<string>> dataRows)
        {
            var html = new System.Text.StringBuilder();
            
            // Build the HTML fragment (the actual table)
            var fragment = new System.Text.StringBuilder();
            fragment.Append("<table style=\"border-collapse:collapse;font-family:Segoe UI,sans-serif;font-size:11pt;\">");
            
            // Header row with bold and background color
            fragment.Append("<tr>");
            foreach (var header in headers)
            {
                fragment.Append("<th style=\"border:1px solid #999;padding:4px 8px;background-color:#f0f0f0;font-weight:bold;text-align:left;\">");
                fragment.Append(System.Net.WebUtility.HtmlEncode(header));
                fragment.Append("</th>");
            }
            fragment.Append("</tr>");
            
            // Data rows
            foreach (var row in dataRows)
            {
                fragment.Append("<tr>");
                foreach (var value in row)
                {
                    fragment.Append("<td style=\"border:1px solid #ccc;padding:4px 8px;white-space:nowrap;\">");
                    fragment.Append(System.Net.WebUtility.HtmlEncode(value));
                    fragment.Append("</td>");
                }
                fragment.Append("</tr>");
            }
            
            fragment.Append("</table>");
            
            // Build the full HTML document required for clipboard HTML format
            var fragmentStr = fragment.ToString();
            var htmlStart = "<html><body><!--StartFragment-->";
            var htmlEnd = "<!--EndFragment--></body></html>";
            var fullHtml = htmlStart + fragmentStr + htmlEnd;
            
            // Build clipboard HTML format header
            // The header must specify byte offsets for StartHTML, EndHTML, StartFragment, EndFragment
            var headerTemplate = "Version:0.9\r\nStartHTML:{0:0000000000}\r\nEndHTML:{1:0000000000}\r\nStartFragment:{2:0000000000}\r\nEndFragment:{3:0000000000}\r\n";
            var headerPlaceholder = string.Format(headerTemplate, 0, 0, 0, 0);
            var headerLength = System.Text.Encoding.UTF8.GetByteCount(headerPlaceholder);
            
            var startHtml = headerLength;
            var startFragment = headerLength + System.Text.Encoding.UTF8.GetByteCount(htmlStart) - "<!--StartFragment-->".Length;
            startFragment = headerLength + System.Text.Encoding.UTF8.GetByteCount("<html><body>");
            var endFragment = startFragment + System.Text.Encoding.UTF8.GetByteCount("<!--StartFragment-->" + fragmentStr);
            var endHtml = headerLength + System.Text.Encoding.UTF8.GetByteCount(fullHtml);
            
            var clipboardHeader = string.Format(headerTemplate, startHtml, endHtml, startFragment, endFragment);
            
            return clipboardHeader + fullHtml;
        }

        /// <summary>
        /// Copies the values from a specific column for all selected rows
        /// </summary>
        private async void CopyColumnValue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string propertyName) return;

            var selectedItems = ViewModel.SelectedAdObjects.ToList();
            if (selectedItems.Count == 0) return;

            // For "Last User + Email", batch-load all emails up front so multi-row copy
            // hits the user cache once instead of N times. Falls back to AD for any users
            // not yet in cache (e.g. seen via Last User scan but never enriched from a user scan).
            Dictionary<string, string?>? emailCache = null;
            if (propertyName == "LastUserWithEmail")
            {
                var usernames = selectedItems
                    .Select(i => i.LastUser)
                    .Where(u => !string.IsNullOrWhiteSpace(u) && !u!.StartsWith("("))
                    .Select(u =>
                    {
                        var name = u!;
                        var slash = name.IndexOf('\\');
                        return slash >= 0 ? name.Substring(slash + 1) : name;
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var lookup = await ViewModel.LookupUserEmailsAsync(usernames);
                emailCache = lookup.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Email,
                    StringComparer.OrdinalIgnoreCase);
            }

            var values = selectedItems
                .Select(item => propertyName == "LastUserWithEmail"
                    ? FormatLastUserWithEmail(item, emailCache!)
                    : GetPropertyValue(item, propertyName))
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            if (values.Count > 0)
            {
                Clipboard.SetText(string.Join(Environment.NewLine, values));
            }
        }

        /// <summary>
        /// Gets a property value from an AdObjectInfo by property name
        /// </summary>
        private string? GetPropertyValue(AdObjectInfo item, string propertyName)
        {
            return propertyName switch
            {
                // Common
                "Name" => item.Name,
                "Description" => item.Description,
                "DistinguishedName" => TargetPath.GetParentPath(item.DistinguishedName),
                "SourcePath" => item.SourcePath,
                "IsEnabled" => item.IsEnabled ? "Enabled" : "Disabled",
                "LastActivity" => item.LastActivity?.ToLocalTime().ToString("g"),
                "LastLogon" => item.LastLogon?.ToLocalTime().ToString("g"),
                "WhenCreated" => item.WhenCreated?.ToLocalTime().ToString("g"),
                "WhenChanged" => item.WhenChanged?.ToLocalTime().ToString("g"),
                "ManagedBy" => item.ManagedBy,
                "IpAddress" => item.IpAddress,
                // Computer
                "DnsHostName" => item.DnsHostName,
                "OperatingSystem" => item.OperatingSystem,
                "OperatingSystemVersion" => item.OperatingSystemVersion,
                "Location" => item.Location,
                "LastUser" => item.LastUser,
                // User
                "SamAccountName" => item.SamAccountName,
                "DisplayName" => item.DisplayName,
                "GivenName" => item.GivenName,
                "Surname" => item.Surname,
                "Mail" => item.Mail,
                "Title" => item.Title,
                "Department" => item.Department,
                "Company" => item.Company,
                "Manager" => item.Manager,
                "Office" => item.Office,
                "TelephoneNumber" => item.TelephoneNumber,
                "Mobile" => item.Mobile,
                "PasswordLastSet" => item.PasswordLastSet?.ToLocalTime().ToString("g"),
                "AccountExpires" => item.AccountExpires?.ToLocalTime().ToString("g"),
                "ComputerHistoryDisplay" => item.ComputerHistoryDisplay,
                // Printer
                "PrinterName" => item.PrinterName,
                "ServerName" => item.ServerName,
                "ShareName" => item.ShareName,
                "UNCName" => item.UNCName,
                "DriverName" => item.DriverName,
                "PortName" => item.PortName,
                "PrinterPriority" => item.PrinterPriority?.ToString(),
                _ => null
            };
        }

        /// <summary>
        /// Returns "{LastUser} - {email}" when an email is cached for the last user,
        /// otherwise just "{LastUser}". Filters out status placeholders like "(no user)".
        /// Uses a pre-built email cache so multi-row copy avoids per-row DB hits.
        /// </summary>
        private static string? FormatLastUserWithEmail(AdObjectInfo item, Dictionary<string, string?> emailCache)
        {
            var user = item.LastUser;
            if (string.IsNullOrWhiteSpace(user) || user.StartsWith("(")) return null;

            var lookup = user;
            var slash = lookup.IndexOf('\\');
            if (slash >= 0) lookup = lookup.Substring(slash + 1);

            return emailCache.TryGetValue(lookup, out var email) && !string.IsNullOrWhiteSpace(email)
                ? $"{user} - {email}"
                : user;
        }

        private (string? Name, string? DnsHostName, string? DistinguishedName, AdObjectType? ObjectType) GetSelectedObjectInfo(object sender)
        {
            // First, use the right-clicked item if available
            if (_rightClickedItem != null)
            {
                return (_rightClickedItem.Name, _rightClickedItem.DnsHostName, _rightClickedItem.DistinguishedName, _rightClickedItem.ObjectType);
            }
            
            // Try to get the object from the context menu's data context
            if (sender is MenuItem menuItem && 
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is DataGrid dataGrid)
            {
                if (dataGrid.SelectedItem is AdObjectInfo adObject)
                {
                    return (adObject.Name, adObject.DnsHostName, adObject.DistinguishedName, adObject.ObjectType);
                }
            }
            
            // Fallback to SelectedAdObjects then SelectedComputers
            var adObj = ViewModel.SelectedAdObjects.FirstOrDefault();
            if (adObj != null) return (adObj.Name, adObj.DnsHostName, adObj.DistinguishedName, adObj.ObjectType);
            
            var comp = ViewModel.SelectedComputers.FirstOrDefault() ?? ViewModel.SelectedComputer;
            if (comp != null) return (comp.Name, comp.DnsHostName, comp.DistinguishedName, AdObjectType.Computer);
            
            return (null, null, null, null);
        }

        private async void ViewAllProperties_Click(object sender, RoutedEventArgs e)
        {
            var info = GetSelectedObjectInfo(sender);
            
            if (info.Name == null)
            {
                MessageBox.Show("Please select an object first.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (string.IsNullOrEmpty(info.DistinguishedName))
            {
                MessageBox.Show("Distinguished name not available for this object.\n" +
                    "Try running the query again with Distinguished Name column enabled.",
                    "Cannot Load Properties", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var ldapService = new Services.LdapService();
                var domainConfig = ViewModel.SelectedDomain;
                
                var properties = await ldapService.GetAllPropertiesAsync(
                    info.DistinguishedName,
                    domainConfig?.Server,
                    null,  // Username - app uses integrated auth
                    null,  // Password
                    domainConfig?.UseLdaps ?? false);

                Mouse.OverrideCursor = null;

                var objectTypeName = info.ObjectType switch
                {
                    AdObjectType.Computer => "Computer",
                    AdObjectType.User => "User",
                    AdObjectType.Printer => "Printer",
                    _ => "Object"
                };

                var dialog = new Views.ComputerPropertiesDialog();
                dialog.Owner = this;
                dialog.LoadProperties(info.Name, info.DistinguishedName, properties, objectTypeName);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"Error loading properties:\n{ex.Message}\n\nDetails: {ex.StackTrace}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewLastUsers_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;

            var dialog = new Views.LastUsersDialog();
            dialog.Owner = this;

            // Handle AdObjectInfo for user profiles
            if (element.DataContext is Models.AdObjectInfo adObj)
            {
                // Use overload with refresh callback (full profile scan) and email lookup callback for AD objects
                // Dialog will load from database and show empty message if no users found
                dialog.LoadUsers(
                    adObj, 
                    async (obj) => await ViewModel.UserProfilesScanForItemAsync(obj),
                    async (usernames) => await ViewModel.LookupUserEmailsAsync(usernames));
                dialog.ShowDialog();
            }
        }

        private void ViewComputerHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;

            // Handle AdObjectInfo for user's computer history
            if (element.DataContext is Models.AdObjectInfo adObj)
            {
                if (adObj.ComputerHistory == null || adObj.ComputerHistory.Count == 0)
                    return;

                var dialog = new Views.ComputerHistoryDialog();
                dialog.Owner = this;
                dialog.LoadHistory(adObj);
                dialog.ShowDialog();
            }
        }

        private void ViewIpHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;

            // Handle AdObjectInfo for IP history
            if (element.DataContext is Models.AdObjectInfo adObj)
            {
                if (adObj.KnownIpAddresses == null || adObj.KnownIpAddresses.Count == 0)
                    return;

                var dialog = new Views.IpHistoryDialog();
                dialog.Owner = this;
                dialog.LoadHistory(adObj);
                dialog.ShowDialog();
            }
        }

        private async void ContextMenuFetchLastUser_Click(object sender, RoutedEventArgs e)
        {
            // Use the batch command which has proper parallel processing and overlay
            if (ViewModel.GetLastUserCommand.CanExecute(null))
            {
                await ViewModel.GetLastUserCommand.ExecuteAsync(null);
            }
        }

        private async void ContextMenuUserProfilesScan_Click(object sender, RoutedEventArgs e)
        {
            // Use the full profile scan command which queries Win32_UserProfile
            if (ViewModel.UserProfilesScanCommand.CanExecute(null))
            {
                await ViewModel.UserProfilesScanCommand.ExecuteAsync(null);
            }
        }

        private void OpenHttp_Click(object sender, RoutedEventArgs e)
        {
            foreach (var adObj in ViewModel.SelectedAdObjects)
            {
                var target = adObj.DnsHostName ?? adObj.Name;
                if (!string.IsNullOrEmpty(target))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = $"http://{target}",
                            UseShellExecute = true
                        });
                    }
                    catch { /* Ignore browser launch failures */ }
                }
            }
        }

        private void OpenHttps_Click(object sender, RoutedEventArgs e)
        {
            foreach (var adObj in ViewModel.SelectedAdObjects)
            {
                var target = adObj.DnsHostName ?? adObj.Name;
                if (!string.IsNullOrEmpty(target))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = $"https://{target}",
                            UseShellExecute = true
                        });
                    }
                    catch { /* Ignore browser launch failures */ }
                }
            }
        }

        private void CreateIncidentTicket_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ViewModel.SelectedAdObjects.ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one item to create a ticket.", 
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Get visible columns from the current DataGrid
            var dataGrid = GetDataGridFromMenuItem(sender);
            var visibleColumns = GetCopyableColumns(dataGrid);

            var dialog = new Views.IncidentTicketWindow(selectedItems, visibleColumns);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void CreateSctaskTicket_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ViewModel.SelectedAdObjects.ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one item to create a ticket.", 
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Get visible columns from the current DataGrid
            var dataGrid = GetDataGridFromMenuItem(sender);
            var visibleColumns = GetCopyableColumns(dataGrid);

            var dialog = new Views.SctaskTicketWindow(selectedItems, visibleColumns);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void CreateEmailFromTemplate_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ViewModel.SelectedAdObjects.ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one item to create an email.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dataGrid = GetDataGridFromMenuItem(sender);
            var visibleColumns = GetCopyableColumns(dataGrid);

            var dialog = new Views.EmailTemplateWindow(selectedItems, visibleColumns, ViewModel.LookupUserEmailsAsync);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void LaunchRdp_Click(object sender, RoutedEventArgs e)
        {
            // Use the right-clicked item, or fall back to first selected computer
            Models.AdObjectInfo? computer = null;
            
            if (_rightClickedItem?.ObjectType == Models.AdObjectType.Computer)
            {
                computer = _rightClickedItem;
            }
            else
            {
                computer = ViewModel.SelectedAdObjects
                    .FirstOrDefault(o => o.ObjectType == Models.AdObjectType.Computer);
            }
            
            if (computer == null) return;
            
            var target = computer.DnsHostName ?? computer.Name;
            if (string.IsNullOrEmpty(target)) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "mstsc",
                    Arguments = $"/v:{target}",
                    UseShellExecute = true
                });
            }
            catch { /* Ignore RDP launch failures */ }
        }

        private async void QuickPing_Click(object sender, RoutedEventArgs e)
        {
            var targets = ViewModel.SelectedAdObjects
                .Where(o => o.ObjectType == Models.AdObjectType.Computer || o.ObjectType == Models.AdObjectType.Printer)
                .ToList();
            
            if (targets.Any())
            {
                await ViewModel.QuickPingAsync(targets);
            }
        }

        private void ContextMenuGpUpdate_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.GpUpdateNormalCommand.Execute(null);
        }

        private void ContextMenuGpUpdateForce_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.GpUpdateForceCommand.Execute(null);
        }

        private void ContextMenuReboot_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RebootComputersCommand.Execute(null);
        }

        private void ContextMenuPushRun_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ShowPushRunCommand.Execute(null);
        }

        private void ContextMenuRemoteCommand_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ShowRemoteCommandCommand.Execute(null);
        }

        private async void ContextMenuVulnerabilities_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.ShowVulnerabilitiesCommand.CanExecute(null))
            {
                await ViewModel.ShowVulnerabilitiesCommand.ExecuteAsync(null);
            }
        }

        private async void ContextMenuPing_Click(object sender, RoutedEventArgs e)
        {
            var targets = ViewModel.SelectedAdObjects
                .Where(o => o.ObjectType == Models.AdObjectType.Computer || o.ObjectType == Models.AdObjectType.Printer)
                .ToList();
            
            if (targets.Any())
            {
                await ViewModel.QuickPingAsync(targets);
            }
        }

        private void ContextMenuTraceroute_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.TracerouteCommand.Execute(null);
        }

        private void ContextMenuPathping_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.PathpingCommand.Execute(null);
        }

        private void ContextMenuDnsForward_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DnsResolveForwardCommand.Execute(null);
        }

        private void ContextMenuDnsReverse_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DnsResolveReverseCommand.Execute(null);
        }

        private void ContextMenuTestPort_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.TestPortCommand.Execute(null);
        }

        private void ContextMenuTestPortSpecific_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string portStr && int.TryParse(portStr, out var port))
            {
                ViewModel.SelectedPort = port;
                ViewModel.TestPortCommand.Execute(null);
            }
        }

        /// <summary>
        /// Bubbles mouse wheel events to parent ScrollViewer to allow scrolling when hovering over nested controls
        /// </summary>
        private void BubbleScrollEvent(object sender, MouseWheelEventArgs e)
        {
            if (sender is not UIElement element) return;
            
            // Find the parent ScrollViewer
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null && parent is not ScrollViewer)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            
            if (parent is ScrollViewer scrollViewer)
            {
                // Create a new event and raise it on the ScrollViewer
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                scrollViewer.RaiseEvent(eventArg);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Generic copy button handler for cell text. Gets text from sibling TextBlock or from Tag.
        /// </summary>
        /// </summary>
        private void CopyCellText_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string? textToCopy = null;
                
                // Try to get text from Tag first (for explicit binding)
                if (button.Tag is string tagText && !string.IsNullOrEmpty(tagText))
                {
                    textToCopy = tagText;
                }
                // Otherwise find sibling TextBlock
                else if (button.Parent is Panel panel)
                {
                    var textBlock = panel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (textBlock != null)
                    {
                        textToCopy = textBlock.Text;
                    }
                }
                
                if (!string.IsNullOrEmpty(textToCopy))
                {
                    try
                    {
                        Clipboard.SetText(textToCopy);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Copy failed: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Copies "{LastUser} - {email}" for the row this button lives in, falling back to just
        /// "{LastUser}" when no email is on file. Mirrors the right-click "Copy Last User + Email"
        /// context menu logic. Uses the AD-fallback email lookup so users seen only via Last User
        /// scans still resolve.
        /// </summary>
        private async void CopyLastUserWithEmail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not AdObjectInfo item) return;

            var user = item.LastUser;
            if (string.IsNullOrWhiteSpace(user) || user.StartsWith("(")) return;

            var lookup = user;
            var slash = lookup.IndexOf('\\');
            if (slash >= 0) lookup = lookup.Substring(slash + 1);

            var resolved = await ViewModel.LookupUserEmailsAsync(new[] { lookup });
            var emailCache = resolved.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Email,
                StringComparer.OrdinalIgnoreCase);
            var formatted = FormatLastUserWithEmail(item, emailCache);

            if (!string.IsNullOrEmpty(formatted))
            {
                try
                {
                    Clipboard.SetText(formatted);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Copy failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Copies just the email for the LastUser of the row this button lives in. Uses the
        /// AD-fallback email lookup so users seen only via Last User scans still resolve. No-op
        /// when no email is on file or in AD.
        /// </summary>
        private async void CopyLastUserEmailOnly_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not AdObjectInfo item) return;

            var user = item.LastUser;
            if (string.IsNullOrWhiteSpace(user) || user.StartsWith("(")) return;

            var lookup = user;
            var slash = lookup.IndexOf('\\');
            if (slash >= 0) lookup = lookup.Substring(slash + 1);

            var resolved = await ViewModel.LookupUserEmailsAsync(new[] { lookup });
            if (!resolved.TryGetValue(lookup, out var info) || string.IsNullOrWhiteSpace(info.Email))
                return;

            try
            {
                Clipboard.SetText(info.Email!);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Bubbles mouse wheel events from nested DataGrids up to the parent ScrollViewer.
        /// Used for Traceroute/Pathping sections where DataGrids are inside a ScrollViewer.
        /// </summary>
        private void NestedDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                // Find the parent ScrollViewer
                var scrollViewer = FindParent<ScrollViewer>(dataGrid);
                if (scrollViewer != null)
                {
                    // Create a new event targeting the scroll viewer
                    var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                        Source = sender
                    };
                    scrollViewer.RaiseEvent(eventArg);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Vista Copy button handler - copies only the trailing numbers from a computer name.
        /// Example: "ANC-WS26519" becomes "26519"
        /// </summary>
        private void VistaCopy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string computerName && !string.IsNullOrEmpty(computerName))
            {
                // Extract trailing numbers using regex
                var match = System.Text.RegularExpressions.Regex.Match(computerName, @"(\d+)$");
                if (match.Success)
                {
                    try
                    {
                        Clipboard.SetText(match.Groups[1].Value);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Vista Copy failed: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Toggle row details visibility for Last User results DataGrid
        /// </summary>
        private void ToggleLastUserRowDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                var dataGridRow = FindParent<DataGridRow>(button);
                if (dataGridRow != null)
                {
                    // Toggle the row details visibility
                    if (dataGridRow.DetailsVisibility == Visibility.Visible)
                    {
                        dataGridRow.DetailsVisibility = Visibility.Collapsed;
                        // Rotate icon back
                        if (button.Content is MaterialDesignThemes.Wpf.PackIcon icon)
                        {
                            icon.RenderTransformOrigin = new Point(0.5, 0.5);
                            icon.RenderTransform = new RotateTransform(0);
                        }
                    }
                    else
                    {
                        dataGridRow.DetailsVisibility = Visibility.Visible;
                        // Rotate icon to show expanded state
                        if (button.Content is MaterialDesignThemes.Wpf.PackIcon icon)
                        {
                            icon.RenderTransformOrigin = new Point(0.5, 0.5);
                            icon.RenderTransform = new RotateTransform(90);
                        }
                    }
                }
            }
        }

        #region Description Editing

        /// <summary>
        /// Context menu handler for editing description. Shows a dialog for input.
        /// </summary>
        private async void EditDescription_ContextMenu_Click(object sender, RoutedEventArgs e)
        {
            // Use the right-clicked item or first selected item
            var adObj = _rightClickedItem;
            if (adObj == null)
            {
                // Try to get from current selection
                var viewModel = DataContext as ViewModels.MainViewModel;
                adObj = viewModel?.SelectedAdObjects?.FirstOrDefault();
            }

            if (adObj == null)
            {
                MessageBox.Show("No item selected.", "Edit Description", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(adObj.DistinguishedName))
            {
                MessageBox.Show("Cannot edit: Missing distinguished name for this object.",
                    "Edit Description", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var vm = DataContext as ViewModels.MainViewModel;
            if (vm?.AdminCredential == null)
            {
                MessageBox.Show("Admin credentials required to update Active Directory.\nPlease log in as admin first.",
                    "Edit Description", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Show input dialog using MaterialDesign's DialogHost
            var currentDescription = adObj.Description ?? string.Empty;
            var result = await ShowEditDescriptionDialogAsync(adObj.Name, currentDescription);
            
            if (result.Confirmed && result.NewDescription != currentDescription)
            {
                // Save the new description
                var originalDesc = adObj.Description;
                adObj.Description = result.NewDescription;
                
                try
                {
                    vm.StatusMessage = $"Updating description for {adObj.Name}...";

                    var ldapService = new Services.LdapService();
                    var (success, error) = await ldapService.UpdateDescriptionAsync(
                        adObj.DistinguishedName,
                        result.NewDescription,
                        server: null,
                        username: $"{vm.AdminCredential.Domain}\\{vm.AdminCredential.UserName}",
                        password: vm.AdminCredential.Password);

                    if (success)
                    {
                        vm.StatusMessage = $"Description updated for {adObj.Name}";
                    }
                    else
                    {
                        adObj.Description = originalDesc;
                        vm.StatusMessage = $"Failed to update description: {error}";
                        MessageBox.Show($"Failed to update description:\n{error}",
                            "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    adObj.Description = originalDesc;
                    vm.StatusMessage = $"Error updating description: {ex.Message}";
                    MessageBox.Show($"Error updating description:\n{ex.Message}",
                        "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Shows a simple input dialog for editing the description
        /// </summary>
        private async Task<(bool Confirmed, string? NewDescription)> ShowEditDescriptionDialogAsync(string objectName, string currentDescription)
        {
            // Create the dialog content
            var stackPanel = new StackPanel { Margin = new Thickness(16) };
            
            var headerText = new TextBlock
            {
                Text = $"Edit Description for {objectName}",
                Style = (Style)FindResource("MaterialDesignHeadline6TextBlock"),
                Margin = new Thickness(0, 0, 0, 16)
            };
            stackPanel.Children.Add(headerText);

            var textBox = new TextBox
            {
                Text = currentDescription,
                MinWidth = 350,
                MaxWidth = 500,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                MaxLength = 1024,
                Style = (Style)FindResource("MaterialDesignOutlinedTextBox")
            };
            MaterialDesignThemes.Wpf.HintAssist.SetHint(textBox, "Description");
            stackPanel.Children.Add(textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Style = (Style)FindResource("MaterialDesignFlatButton"),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            cancelButton.Click += (s, e) => MaterialDesignThemes.Wpf.DialogHost.CloseDialogCommand.Execute(false, null);
            buttonPanel.Children.Add(cancelButton);

            var saveButton = new Button
            {
                Content = "Save",
                Style = (Style)FindResource("MaterialDesignRaisedButton"),
                IsDefault = true
            };
            saveButton.Click += (s, e) => MaterialDesignThemes.Wpf.DialogHost.CloseDialogCommand.Execute(true, null);
            buttonPanel.Children.Add(saveButton);

            stackPanel.Children.Add(buttonPanel);

            // Show the dialog
            var dialogResult = await MaterialDesignThemes.Wpf.DialogHost.Show(stackPanel, "RootDialog");
            
            if (dialogResult is bool confirmed && confirmed)
            {
                return (true, textBox.Text);
            }
            
            return (false, null);
        }

        /// <summary>
        /// Context menu handler for enabling/disabling an AD account
        /// </summary>
        private async void EnableDisable_ContextMenu_Click(object sender, RoutedEventArgs e)
        {
            var adObj = _rightClickedItem;
            if (adObj == null)
            {
                MessageBox.Show("No item selected.", "Enable/Disable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Only works for computers and users
            if (adObj.ObjectType == Models.AdObjectType.Printer)
            {
                MessageBox.Show("Cannot enable/disable printers.", "Enable/Disable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(adObj.DistinguishedName))
            {
                MessageBox.Show("Cannot modify: Missing distinguished name for this object.",
                    "Enable/Disable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var vm = DataContext as ViewModels.MainViewModel;
            if (vm?.AdminCredential == null)
            {
                MessageBox.Show("Admin credentials required to update Active Directory.\nPlease log in as admin first.",
                    "Enable/Disable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var action = adObj.IsEnabled ? "disable" : "enable";
            var actionPast = adObj.IsEnabled ? "disabled" : "enabled";
            var typeName = adObj.ObjectType == Models.AdObjectType.Computer ? "computer" : "user";

            // Confirm the action
            var confirmResult = MessageBox.Show(
                $"Are you sure you want to {action} the {typeName} '{adObj.Name}'?",
                $"Confirm {action}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            try
            {
                vm.StatusMessage = $"{(adObj.IsEnabled ? "Disabling" : "Enabling")} {adObj.Name}...";

                var ldapService = new Services.LdapService();
                var (success, error) = await ldapService.SetAccountEnabledAsync(
                    adObj.DistinguishedName,
                    !adObj.IsEnabled,  // Toggle the current state
                    server: null,
                    username: $"{vm.AdminCredential.Domain}\\{vm.AdminCredential.UserName}",
                    password: vm.AdminCredential.Password);

                if (success)
                {
                    // Update the local object state
                    adObj.IsEnabled = !adObj.IsEnabled;
                    vm.StatusMessage = $"{adObj.Name} has been {actionPast}";
                }
                else
                {
                    vm.StatusMessage = $"Failed to {action} {adObj.Name}: {error}";
                    MessageBox.Show($"Failed to {action} account:\n{error}",
                        "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error {action}ing account:\n{ex.Message}",
                    "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Context menu handler for deleting a computer from Active Directory
        /// </summary>
        private async void DeleteComputer_ContextMenu_Click(object sender, RoutedEventArgs e)
        {
            var adObj = _rightClickedItem;
            if (adObj == null)
            {
                MessageBox.Show("No item selected.", "Delete Computer", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Only works for computers
            if (adObj.ObjectType != Models.AdObjectType.Computer)
            {
                MessageBox.Show("This action is only available for computers.", "Delete Computer", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(adObj.DistinguishedName))
            {
                MessageBox.Show("Cannot delete: Missing distinguished name for this object.",
                    "Delete Computer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var vm = DataContext as ViewModels.MainViewModel;
            if (vm == null || vm.AdminCredential == null)
            {
                MessageBox.Show("Admin credentials required to delete from Active Directory.\nPlease log in as admin first.",
                    "Delete Computer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                vm.StatusMessage = $"Checking {adObj.Name}...";

                var ldapService = new Services.LdapService();
                var username = $"{vm.AdminCredential.Domain}\\{vm.AdminCredential.UserName}";
                var password = vm.AdminCredential.Password;

                // Check if the object is protected from accidental deletion
                var (isProtected, protectedError) = await ldapService.IsProtectedFromDeletionAsync(
                    adObj.DistinguishedName, server: null, username, password);

                if (protectedError != null)
                {
                    vm.StatusMessage = $"Error checking protection status: {protectedError}";
                    MessageBox.Show($"Error checking object status:\n{protectedError}",
                        "Delete Computer", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (isProtected)
                {
                    var unprotectResult = MessageBox.Show(
                        $"The computer '{adObj.Name}' is protected from accidental deletion.\n\n" +
                        "Do you want to remove the protection and proceed with deletion?",
                        "Protected Object",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (unprotectResult != MessageBoxResult.Yes)
                        return;

                    // Remove protection
                    vm.StatusMessage = $"Removing protection from {adObj.Name}...";
                    var (unprotectSuccess, unprotectError) = await ldapService.SetProtectedFromDeletionAsync(
                        adObj.DistinguishedName, false, server: null, username, password);

                    if (!unprotectSuccess)
                    {
                        vm.StatusMessage = $"Failed to remove protection: {unprotectError}";
                        MessageBox.Show($"Failed to remove deletion protection:\n{unprotectError}",
                            "Delete Computer", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Check if the object has child objects
                var (hasChildren, childCount, childError) = await ldapService.HasChildObjectsAsync(
                    adObj.DistinguishedName, server: null, username, password);

                if (childError != null)
                {
                    vm.StatusMessage = $"Error checking for child objects: {childError}";
                    MessageBox.Show($"Error checking object:\n{childError}",
                        "Delete Computer", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool deleteSubtree = false;
                if (hasChildren)
                {
                    var subtreeResult = MessageBox.Show(
                        $"The computer '{adObj.Name}' has {childCount} child object(s).\n\n" +
                        "Do you want to delete the computer and all its child objects?",
                        "Delete Subtree",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (subtreeResult != MessageBoxResult.Yes)
                        return;

                    deleteSubtree = true;
                }
                else
                {
                    // Standard confirmation
                    var confirmResult = MessageBox.Show(
                        $"Are you sure you want to permanently delete the computer '{adObj.Name}' from Active Directory?\n\n" +
                        "This action cannot be undone.",
                        "Confirm Delete",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirmResult != MessageBoxResult.Yes)
                        return;
                }

                // Perform the deletion
                vm.StatusMessage = $"Deleting {adObj.Name}...";
                var (deleteSuccess, deleteError) = await ldapService.DeleteObjectAsync(
                    adObj.DistinguishedName, deleteSubtree, server: null, username, password);

                if (deleteSuccess)
                {
                    vm.StatusMessage = $"Computer '{adObj.Name}' has been deleted from Active Directory";
                    
                    // Remove from the current selection (single + multi-select collections)
                    vm.SelectedAdObjects.Remove(adObj);
                    vm.SelectedComputers.Remove(adObj);
                    if (ReferenceEquals(vm.SelectedComputer, adObj))
                        vm.SelectedComputer = null;
                    
                    // Remove from the tab's results. This updates BOTH the full backing list
                    // and the displayed/filtered list and rebuilds the grid, preventing the
                    // index mismatch that caused the wrong computer to be referenced.
                    foreach (var tab in vm.OutputTabs)
                    {
                        if (tab.RemoveAdObject(adObj))
                            break;
                    }
                    
                    MessageBox.Show($"Computer '{adObj.Name}' has been deleted.",
                        "Delete Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    vm.StatusMessage = $"Failed to delete {adObj.Name}: {deleteError}";
                    MessageBox.Show($"Failed to delete computer:\n{deleteError}",
                        "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error deleting computer:\n{ex.Message}",
                    "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        /// <summary>
        /// Helper method to find a parent of a specific type in the visual tree
        /// </summary>
        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            
            if (parentObject == null) return null;
            
            if (parentObject is T parent)
                return parent;
            
            return FindParent<T>(parentObject);
        }

        #endregion

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle Ctrl+A globally
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // If a TextBox is focused, let it handle Ctrl+A natively (select all text)
                if (Keyboard.FocusedElement is TextBox)
                    return;
                
                // Otherwise, select all items in the DataGrid
                var dataGrid = FindVisualChild<DataGrid>(this);
                if (dataGrid != null)
                {
                    _isBatchSelecting = true;
                    try
                    {
                        dataGrid.SelectAll();
                    }
                    finally
                    {
                        _isBatchSelecting = false;
                        ProcessSelectionChange(dataGrid);
                    }
                }
                e.Handled = true;
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Clear focus from TextBox when clicking outside of it
            if (Keyboard.FocusedElement is TextBox)
            {
                // Walk up the visual tree from the clicked element
                var source = e.OriginalSource as DependencyObject;
                DataGrid? clickedDataGrid = null;
                
                while (source != null)
                {
                    // If clicking on a TextBox, don't change focus
                    if (source is TextBox)
                        return;
                    
                    // Track if we're clicking on a DataGrid
                    if (source is DataGrid dg)
                        clickedDataGrid = dg;
                    
                    source = VisualTreeHelper.GetParent(source);
                }
                
                // If clicking on a DataGrid, focus it so Ctrl+A works for list items
                if (clickedDataGrid != null)
                {
                    clickedDataGrid.Focus();
                }
                else
                {
                    // Clicked on a true background area, clear focus
                    Keyboard.ClearFocus();
                }
            }
        }

        #region Tab Selection and Layout

        private ViewModels.OutputTabViewModel? _currentVisibilityTrackedTab;

        /// <summary>
        /// Force DataGrid column width recalculation when tab selection changes.
        /// Fixes issue where star-sized columns are too thin on first display.
        /// </summary>
        private void OutputTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only handle events from our TabControl, not from nested controls
            if (e.OriginalSource != sender) return;
            
            if (sender is TabControl tabControl)
            {
                // Unsubscribe from previous tab's visibility changes
                if (_currentVisibilityTrackedTab != null)
                {
                    _currentVisibilityTrackedTab.ColumnVisibilityChanged -= OnColumnVisibilityChanged;
                }
                
                // Subscribe to new tab's visibility changes
                if (tabControl.SelectedItem is ViewModels.OutputTabViewModel newTab)
                {
                    _currentVisibilityTrackedTab = newTab;
                    _currentVisibilityTrackedTab.ColumnVisibilityChanged += OnColumnVisibilityChanged;
                }
                
                // Defer the layout update to after the tab content is fully rendered
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
                {
                    // Find the DataGrid in the current tab's content
                    var selectedTab = tabControl.SelectedItem;
                    if (selectedTab == null) return;
                    
                    var container = tabControl.ItemContainerGenerator.ContainerFromItem(selectedTab);
                    if (container == null) return;
                    
                    var dataGrid = FindVisualChild<DataGrid>(tabControl);
                    if (dataGrid != null)
                    {
                        RefreshDataGridLayout(dataGrid);
                    }
                }));
            }
        }

        /// <summary>
        /// Called when a tab's column visibility (ShowsUsers, ShowsComputers) changes.
        /// Forces the DataGrid to recalculate column widths.
        /// </summary>
        private void OnColumnVisibilityChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                var dataGrid = FindVisualChild<DataGrid>(this);
                if (dataGrid != null)
                {
                    RefreshDataGridLayout(dataGrid);
                }
            }));
        }

        /// <summary>
        /// Refreshes a DataGrid's layout to fix column width issues.
        /// </summary>
        private void RefreshDataGridLayout(DataGrid dataGrid)
        {
            dataGrid.InvalidateMeasure();
            dataGrid.InvalidateArrange();
            dataGrid.UpdateLayout();
            
            // Also reset star-width columns to trigger proper sizing
            foreach (var column in dataGrid.Columns)
            {
                if (column.Width.IsStar)
                {
                    var starValue = column.Width.Value;
                    column.Width = new DataGridLength(starValue, DataGridLengthUnitType.Star);
                }
            }
        }

        #endregion

        #region Tab Drag-Drop Reordering

        private Point _tabDragStartPoint;

        private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _tabDragStartPoint = e.GetPosition(null);
        }

        private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            
            var currentPos = e.GetPosition(null);
            var diff = _tabDragStartPoint - currentPos;

            // Check if we've moved enough to start a drag
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is TabItem tabItem && tabItem.DataContext is ViewModels.OutputTabViewModel tabVm)
                {
                    // Don't allow dragging the main Domain Results tab
                    if (!tabVm.CanClose) return;

                    var data = new DataObject(typeof(ViewModels.OutputTabViewModel), tabVm);
                    DragDrop.DoDragDrop(tabItem, data, DragDropEffects.Move);
                }
            }
        }

        private void TabItem_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(ViewModels.OutputTabViewModel)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            // Live reorder during drag
            var sourceTab = e.Data.GetData(typeof(ViewModels.OutputTabViewModel)) as ViewModels.OutputTabViewModel;
            if (sourceTab == null) return;

            if (sender is TabItem targetTabItem && targetTabItem.DataContext is ViewModels.OutputTabViewModel targetTab)
            {
                if (sourceTab == targetTab) return;

                var sourceIndex = ViewModel.OutputTabs.IndexOf(sourceTab);
                var targetIndex = ViewModel.OutputTabs.IndexOf(targetTab);

                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    ViewModel.OutputTabs.Move(sourceIndex, targetIndex);
                }
            }
        }

        private void TabItem_Drop(object sender, DragEventArgs e)
        {
            // Move already happened in DragOver, just mark as handled
            e.Handled = true;
        }

        #endregion

        private async void SubfoldersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is FolderItem folder)
            {
                await ViewModel.OpenFolderCommand.ExecuteAsync(folder);
                listBox.SelectedItem = null; // Reset selection so you can click the same folder again if needed
            }
        }

        #region Network History Panel

        private bool _isDraggingGrip;
        private Point _gripDragStart;
        private double _gripDragStartHeight;
        private const double GripDragThreshold = 3;
        private const double MinPanelHeight = 60;

        private void NetworkPanelGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingGrip = false;
            _gripDragStart = e.GetPosition(this);
            _gripDragStartHeight = NetworkHistoryScrollViewer.ActualHeight;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void NetworkPanelGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var current = e.GetPosition(this);
            var delta = _gripDragStart.Y - current.Y;

            if (!_isDraggingGrip && Math.Abs(delta) > GripDragThreshold)
            {
                _isDraggingGrip = true;
                // If panel was collapsed, start from a sensible initial height
                if (_gripDragStartHeight < MinPanelHeight)
                    _gripDragStartHeight = MinPanelHeight;
            }

            if (_isDraggingGrip)
            {
                var newHeight = Math.Max(0, _gripDragStartHeight + delta);
                if (newHeight < MinPanelHeight)
                {
                    // Snap to collapsed
                    ViewModel.NetworkPanelHeight = 0;
                    ViewModel.IsNetworkPanelExpanded = false;
                }
                else
                {
                    ViewModel.NetworkPanelHeight = Math.Min(newHeight, 500);
                    ViewModel.IsNetworkPanelExpanded = true;
                }
            }
        }

        private void NetworkPanelGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).ReleaseMouseCapture();

            if (!_isDraggingGrip)
            {
                // Click behavior: toggle expand/collapse
                if (ViewModel.IsNetworkPanelExpanded)
                {
                    ViewModel._lastNetworkPanelHeight = NetworkHistoryScrollViewer.ActualHeight > MinPanelHeight ? NetworkHistoryScrollViewer.ActualHeight : 180;
                    ViewModel.NetworkPanelHeight = 0;
                    ViewModel.IsNetworkPanelExpanded = false;
                }
                else
                {
                    ViewModel.NetworkPanelHeight = ViewModel._lastNetworkPanelHeight;
                    ViewModel.IsNetworkPanelExpanded = true;
                }
            }
            else
            {
                // After drag, save the current height
                if (ViewModel.IsNetworkPanelExpanded && NetworkHistoryScrollViewer.ActualHeight > MinPanelHeight)
                {
                    ViewModel._lastNetworkPanelHeight = NetworkHistoryScrollViewer.ActualHeight;
                }
            }

            _isDraggingGrip = false;
            e.Handled = true;
        }

        #endregion

        #region Group Drag-Drop Reordering

        // Track if we're in a drag operation to prevent saving on every move
        private bool _isDraggingDomainGroup;
        private bool _isDraggingComputerGroup;
        private bool _isDraggingUserGroup;
        private bool _isDraggingPrinterGroup;

        private void DomainGroupDragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Models.TargetGroup group)
            {
                _isDraggingDomainGroup = true;
                DragDrop.DoDragDrop(element, new DataObject("DomainGroup", group), DragDropEffects.Move);
                _isDraggingDomainGroup = false;
                ViewModel.SaveDomainGroupOrder();
                e.Handled = true;
            }
        }

        private void ComputerGroupDragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Models.CustomGroup group)
            {
                _isDraggingComputerGroup = true;
                DragDrop.DoDragDrop(element, new DataObject("ComputerGroup", group), DragDropEffects.Move);
                _isDraggingComputerGroup = false;
                ViewModel.SaveComputerGroupOrder();
                e.Handled = true;
            }
        }

        private void UserGroupDragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Models.CustomGroup group)
            {
                _isDraggingUserGroup = true;
                DragDrop.DoDragDrop(element, new DataObject("UserGroup", group), DragDropEffects.Move);
                _isDraggingUserGroup = false;
                ViewModel.SaveUserGroupOrder();
                e.Handled = true;
            }
        }

        private void PrinterGroupDragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Models.CustomGroup group)
            {
                _isDraggingPrinterGroup = true;
                DragDrop.DoDragDrop(element, new DataObject("PrinterGroup", group), DragDropEffects.Move);
                _isDraggingPrinterGroup = false;
                ViewModel.SavePrinterGroupOrder();
                e.Handled = true;
            }
        }

        private void Groups_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            // Live reorder during drag
            if (sender is ItemsControl itemsControl)
            {
                var dropIndex = GetDropIndex(itemsControl, e.GetPosition(itemsControl));

                if (e.Data.GetDataPresent("DomainGroup") && _isDraggingDomainGroup)
                {
                    var group = e.Data.GetData("DomainGroup") as Models.TargetGroup;
                    if (group != null)
                    {
                        var currentIndex = ViewModel.SavedTargetGroups.IndexOf(group);
                        if (currentIndex >= 0 && currentIndex != dropIndex)
                        {
                            ViewModel.SavedTargetGroups.Move(currentIndex, dropIndex);
                        }
                    }
                }
                else if (e.Data.GetDataPresent("ComputerGroup") && _isDraggingComputerGroup)
                {
                    var group = e.Data.GetData("ComputerGroup") as Models.CustomGroup;
                    if (group != null)
                    {
                        var currentIndex = ViewModel.ComputerGroups.IndexOf(group);
                        if (currentIndex >= 0 && currentIndex != dropIndex)
                        {
                            ViewModel.ComputerGroups.Move(currentIndex, dropIndex);
                        }
                    }
                }
                else if (e.Data.GetDataPresent("UserGroup") && _isDraggingUserGroup)
                {
                    var group = e.Data.GetData("UserGroup") as Models.CustomGroup;
                    if (group != null)
                    {
                        var currentIndex = ViewModel.UserGroups.IndexOf(group);
                        if (currentIndex >= 0 && currentIndex != dropIndex)
                        {
                            ViewModel.UserGroups.Move(currentIndex, dropIndex);
                        }
                    }
                }
                else if (e.Data.GetDataPresent("PrinterGroup") && _isDraggingPrinterGroup)
                {
                    var group = e.Data.GetData("PrinterGroup") as Models.CustomGroup;
                    if (group != null)
                    {
                        var currentIndex = ViewModel.PrinterGroups.IndexOf(group);
                        if (currentIndex >= 0 && currentIndex != dropIndex)
                        {
                            ViewModel.PrinterGroups.Move(currentIndex, dropIndex);
                        }
                    }
                }
            }
        }

        private void DomainGroups_Drop(object sender, DragEventArgs e)
        {
            // Order is saved in MouseDown after DoDragDrop returns
            e.Handled = true;
        }

        private void ComputerGroups_Drop(object sender, DragEventArgs e)
        {
            // Order is saved in MouseDown after DoDragDrop returns
            e.Handled = true;
        }

        private void UserGroups_Drop(object sender, DragEventArgs e)
        {
            // Order is saved in MouseDown after DoDragDrop returns
            e.Handled = true;
        }

        private void PrinterGroups_Drop(object sender, DragEventArgs e)
        {
            // Order is saved in MouseDown after DoDragDrop returns
            e.Handled = true;
        }

        private int GetDropIndex(ItemsControl itemsControl, Point dropPosition)
        {
            // Find which item we're dropping on
            var itemCount = itemsControl.Items.Count;
            if (itemCount == 0) return 0;

            double accumulatedHeight = 0;
            for (int i = 0; i < itemCount; i++)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                var itemHeight = container.ActualHeight;
                var itemMidpoint = accumulatedHeight + (itemHeight / 2);

                if (dropPosition.Y < itemMidpoint)
                {
                    return i;
                }

                accumulatedHeight += itemHeight;
            }

            return itemCount - 1;
        }

        #endregion
    }
}
