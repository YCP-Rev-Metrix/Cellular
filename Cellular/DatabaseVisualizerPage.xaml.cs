using System.Collections.Generic;
using System.Reflection;
using Cellular.Data;
using Cellular.ViewModel;
using Microsoft.Maui.Controls;

namespace Cellular;

public partial class DatabaseVisualizerPage : ContentPage
{
    private readonly SQLite.SQLiteAsyncConnection _db;
    private readonly Dictionary<string, Type> _tables = new()
    {
        { "User", typeof(User) },
        { "Ball", typeof(Ball) },
        { "Event", typeof(Event) },
        { "Establishment", typeof(Establishment) },
        { "Session", typeof(Session) },
        { "Game", typeof(Game) },
        { "BowlingFrame", typeof(BowlingFrame) },
        { "Shot", typeof(Shot) }
    };

    public DatabaseVisualizerPage()
    {
        InitializeComponent();
        _db = new CellularDatabase().GetConnection();
        LoadTableNames();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (TablePicker.SelectedIndex >= 0)
            LoadTableData(TablePicker.SelectedIndex);
    }

    private void LoadTableNames()
    {
        TablePicker.ItemsSource = new List<string>(_tables.Keys);
        TablePicker.SelectedIndex = 0;
    }

    private void OnTableSelected(object? sender, EventArgs e)
    {
        if (TablePicker.SelectedIndex < 0) return;
        LoadTableData(TablePicker.SelectedIndex);
    }

    private async void LoadTableData(int tableIndex)
    {
        var tableNames = new List<string>(_tables.Keys);
        var tableName = tableNames[tableIndex];
        var entityType = _tables[tableName];

        DataContainer.Children.Clear();
        RowCountLabel.Text = "Loading...";

        try
        {
            var items = await QueryAllAsync(entityType);
            RowCountLabel.Text = $"{tableName}: {items.Count} row(s)";

            for (int i = 0; i < items.Count; i++)
            {
                var card = BuildRowCard(tableName, i + 1, items[i]);
                DataContainer.Children.Add(card);
            }
        }
        catch (Exception ex)
        {
            RowCountLabel.Text = $"Error: {ex.Message}";
            DataContainer.Children.Add(new Label
            {
                Text = ex.ToString(),
                TextColor = Colors.OrangeRed,
                FontSize = 12,
                Margin = new Thickness(0, 8)
            });
        }
    }

    private async Task<IReadOnlyList<object>> QueryAllAsync(Type entityType)
    {
        var method = GetType().GetMethod(nameof(QueryTableAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entityType);
        var task = (Task)method.Invoke(this, null)!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task);
        var list = (System.Collections.IList)result!;
        var objects = new List<object>();
        for (int i = 0; i < list.Count; i++)
            objects.Add(list[i]!);
        return objects;
    }

    private async Task<List<T>> QueryTableAsync<T>() where T : new()
    {
        return await _db.Table<T>().ToListAsync();
    }

    private Frame BuildRowCard(string tableName, int rowIndex, object row)
    {
        var props = row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var stack = new VerticalStackLayout { Spacing = 6 };

        var header = new Label
        {
            Text = $"{tableName} #{rowIndex}",
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            TextColor = Color.FromArgb("#DFD8F7")
        };
        stack.Children.Add(header);

        foreach (var p in props)
        {
            var val = p.GetValue(row);
            var text = val == null ? "null" : (val is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm") : val.ToString());
            var line = new Label
            {
                Text = $"{p.Name}: {text}",
                FontSize = 13,
                TextColor = Color.FromArgb("#C8C8C8")
            };
            stack.Children.Add(line);
        }

        return new Frame
        {
            Content = stack,
            BorderColor = Color.FromArgb("#404040"),
            BackgroundColor = Color.FromArgb("#212121"),
            Padding = new Thickness(14, 10),
            CornerRadius = 8,
            HasShadow = false
        };
    }
}
