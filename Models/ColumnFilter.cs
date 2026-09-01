using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Filter comparison types for text columns
    /// </summary>
    public enum TextFilterType
    {
        Contains,
        Is,
        StartsWith,
        EndsWith,
        HasValue
    }

    /// <summary>
    /// Filter comparison types for date columns
    /// </summary>
    public enum DateFilterType
    {
        Before,
        After,
        WithinDays,
        OlderThanDays
    }

    /// <summary>
    /// The type of data a column contains
    /// </summary>
    public enum ColumnDataType
    {
        Text,
        Date,
        Boolean
    }

    /// <summary>
    /// Represents a single filter condition for a column
    /// </summary>
    public partial class FilterCondition : ObservableObject
    {
        [ObservableProperty]
        private TextFilterType _textFilterType = TextFilterType.Contains;

        [ObservableProperty]
        private DateFilterType _dateFilterType = DateFilterType.WithinDays;

        [ObservableProperty]
        private string _textValue = string.Empty;

        [ObservableProperty]
        private int? _daysValue;

        [ObservableProperty]
        private DateTime? _dateValue;

        [ObservableProperty]
        private bool _boolValue = true;

        /// <summary>
        /// Event raised when any filter value changes
        /// </summary>
        public event EventHandler? FilterChanged;

        partial void OnTextFilterTypeChanged(TextFilterType value) => FilterChanged?.Invoke(this, EventArgs.Empty);
        partial void OnDateFilterTypeChanged(DateFilterType value) => FilterChanged?.Invoke(this, EventArgs.Empty);
        partial void OnTextValueChanged(string value) => FilterChanged?.Invoke(this, EventArgs.Empty);
        partial void OnDaysValueChanged(int? value) => FilterChanged?.Invoke(this, EventArgs.Empty);
        partial void OnDateValueChanged(DateTime? value) => FilterChanged?.Invoke(this, EventArgs.Empty);
        partial void OnBoolValueChanged(bool value) => FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Represents all filters for a single column
    /// </summary>
    public partial class ColumnFilter : ObservableObject
    {
        public string ColumnName { get; }
        public string PropertyName { get; }
        public ColumnDataType DataType { get; }

        [ObservableProperty]
        private ObservableCollection<FilterCondition> _conditions = new();

        [ObservableProperty]
        private bool _isActive;

        /// <summary>
        /// Event raised when any filter condition changes
        /// </summary>
        public event EventHandler? FilterChanged;

        public ColumnFilter(string columnName, string propertyName, ColumnDataType dataType)
        {
            ColumnName = columnName;
            PropertyName = propertyName;
            DataType = dataType;
        }

        [RelayCommand]
        public void AddCondition()
        {
            var condition = new FilterCondition();
            condition.FilterChanged += (s, e) => OnFilterConditionChanged();
            Conditions.Add(condition);
            UpdateIsActive();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        public void RemoveCondition(FilterCondition condition)
        {
            if (condition != null)
            {
                condition.FilterChanged -= (s, e) => OnFilterConditionChanged();
                Conditions.Remove(condition);
                UpdateIsActive();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [RelayCommand]
        public void ClearConditions()
        {
            foreach (var condition in Conditions)
            {
                condition.FilterChanged -= (s, e) => OnFilterConditionChanged();
            }
            Conditions.Clear();
            UpdateIsActive();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnFilterConditionChanged()
        {
            UpdateIsActive();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateIsActive()
        {
            IsActive = Conditions.Any(c => 
                !string.IsNullOrEmpty(c.TextValue) || 
                c.DaysValue.HasValue || 
                c.DateValue.HasValue ||
                c.TextFilterType == TextFilterType.HasValue);
        }

        /// <summary>
        /// Tests if an object passes all filter conditions (AND logic)
        /// </summary>
        public bool PassesFilter(object? value)
        {
            if (!Conditions.Any())
                return true;

            // AND logic - must pass all conditions
            foreach (var condition in Conditions)
            {
                if (!PassesCondition(value, condition))
                    return false;
            }
            return true;
        }

        private bool PassesCondition(object? value, FilterCondition condition)
        {
            switch (DataType)
            {
                case ColumnDataType.Text:
                    return PassesTextCondition(value as string, condition);

                case ColumnDataType.Date:
                    return PassesDateCondition(value as DateTime?, condition);

                case ColumnDataType.Boolean:
                    return PassesBoolCondition(value as bool?, condition);

                default:
                    return true;
            }
        }

        private bool PassesTextCondition(string? value, FilterCondition condition)
        {
            if (condition.TextFilterType == TextFilterType.HasValue)
            {
                return !string.IsNullOrWhiteSpace(value);
            }

            if (string.IsNullOrEmpty(condition.TextValue))
                return true; // No filter value = pass

            if (string.IsNullOrEmpty(value))
                return false; // Has filter but no value = fail

            return condition.TextFilterType switch
            {
                TextFilterType.Is => value.Equals(condition.TextValue, StringComparison.OrdinalIgnoreCase),
                TextFilterType.Contains => value.IndexOf(condition.TextValue, StringComparison.OrdinalIgnoreCase) >= 0,
                TextFilterType.StartsWith => value.StartsWith(condition.TextValue, StringComparison.OrdinalIgnoreCase),
                TextFilterType.EndsWith => value.EndsWith(condition.TextValue, StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        private bool PassesDateCondition(DateTime? value, FilterCondition condition)
        {
            switch (condition.DateFilterType)
            {
                case DateFilterType.WithinDays:
                    if (!condition.DaysValue.HasValue)
                        return true;
                    if (!value.HasValue)
                        return false;
                    var withinCutoff = DateTime.Now.AddDays(-condition.DaysValue.Value);
                    return value.Value >= withinCutoff;

                case DateFilterType.OlderThanDays:
                    if (!condition.DaysValue.HasValue)
                        return true;
                    if (!value.HasValue)
                        return true; // No logon date = older than anything
                    var olderCutoff = DateTime.Now.AddDays(-condition.DaysValue.Value);
                    return value.Value < olderCutoff;

                case DateFilterType.Before:
                    if (!condition.DateValue.HasValue)
                        return true;
                    if (!value.HasValue)
                        return false;
                    return value.Value < condition.DateValue.Value;

                case DateFilterType.After:
                    if (!condition.DateValue.HasValue)
                        return true;
                    if (!value.HasValue)
                        return false;
                    return value.Value > condition.DateValue.Value;

                default:
                    return true;
            }
        }

        private bool PassesBoolCondition(bool? value, FilterCondition condition)
        {
            // For boolean, just check if value matches expected
            return value == condition.BoolValue;
        }
    }

    /// <summary>
    /// Manages all column filters for a results grid
    /// </summary>
    public partial class ColumnFilterManager : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<ColumnFilter> _filters = new();

        [ObservableProperty]
        private bool _hasActiveFilters;

        [ObservableProperty]
        private int _activeFilterCount;
        
        // Dictionary for O(1) filter lookups by property name
        private readonly Dictionary<string, ColumnFilter> _filtersByProperty = 
            new(StringComparer.OrdinalIgnoreCase);
        
        // Cache for compiled property accessors to avoid reflection in hot path
        private static readonly Dictionary<(Type, string), Func<object, object?>> _propertyAccessorCache = new();
        private static readonly object _cacheLock = new();

        /// <summary>
        /// Event raised when any filter changes
        /// </summary>
        public event EventHandler? FiltersChanged;

        public void AddFilter(ColumnFilter filter)
        {
            filter.FilterChanged += OnFilterChanged;
            Filters.Add(filter);
            _filtersByProperty[filter.PropertyName] = filter;
        }

        public ColumnFilter? GetFilter(string propertyName)
        {
            return _filtersByProperty.TryGetValue(propertyName, out var filter) ? filter : null;
        }

        [RelayCommand]
        public void ClearAllFilters()
        {
            foreach (var filter in Filters)
            {
                filter.ClearConditions();
            }
            UpdateActiveState();
            FiltersChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnFilterChanged(object? sender, EventArgs e)
        {
            UpdateActiveState();
            FiltersChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateActiveState()
        {
            ActiveFilterCount = Filters.Count(f => f.IsActive);
            HasActiveFilters = ActiveFilterCount > 0;
        }

        /// <summary>
        /// Tests if an object passes all active filters (AND logic between columns)
        /// </summary>
        public bool PassesAllFilters<T>(T item)
        {
            if (item == null)
                return false;

            var type = typeof(T);
            foreach (var filter in Filters.Where(f => f.IsActive))
            {
                var accessor = GetOrCreateAccessor(type, filter.PropertyName);
                if (accessor == null)
                    continue;

                var value = accessor(item);
                if (!filter.PassesFilter(value))
                    return false;
            }
            return true;
        }
        
        /// <summary>
        /// Gets or creates a compiled property accessor for fast property access
        /// </summary>
        private static Func<object, object?>? GetOrCreateAccessor(Type type, string propertyName)
        {
            var key = (type, propertyName);
            
            lock (_cacheLock)
            {
                if (_propertyAccessorCache.TryGetValue(key, out var cached))
                    return cached;
            }
            
            var property = type.GetProperty(propertyName, 
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            
            if (property == null)
            {
                lock (_cacheLock)
                {
                    _propertyAccessorCache[key] = null!;
                }
                return null;
            }
            
            // Compile a fast accessor: (object obj) => (object?)((T)obj).Property
            var param = Expression.Parameter(typeof(object), "obj");
            var cast = Expression.Convert(param, type);
            var propAccess = Expression.Property(cast, property);
            var boxed = Expression.Convert(propAccess, typeof(object));
            var lambda = Expression.Lambda<Func<object, object?>>(boxed, param);
            var accessor = lambda.Compile();
            
            lock (_cacheLock)
            {
                _propertyAccessorCache[key] = accessor;
            }
            
            return accessor;
        }
    }
}
