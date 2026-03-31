using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Cellular.Data;
using Cellular.Cloud_API;
using Cellular.Services;
using Cellular.ViewModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Cellular;

public partial class DatabaseVisualizerPage : ContentPage
{
    private readonly SQLite.SQLiteAsyncConnection _db;
    private readonly Dictionary<string, Type> _localTables = new()
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

    private readonly Dictionary<string, (EntityType entityType, Type cloudModelType)> _cloudTables = new()
    {
        // "User" is intentionally not supported here (no matching cloud endpoint in the current API).
        { "Ball", (EntityType.Ball, typeof(Cellular.Cloud_API.Models.Ball)) },
        { "Establishment", (EntityType.Establishment, typeof(Cellular.Cloud_API.Models.Establishment)) },
        { "Event", (EntityType.Event, typeof(Cellular.Cloud_API.Models.Event)) },
        { "Session", (EntityType.Session, typeof(Cellular.Cloud_API.Models.Session)) },
        { "Game", (EntityType.Game, typeof(Cellular.Cloud_API.Models.Game)) },
        { "BowlingFrame", (EntityType.Frame, typeof(Cellular.Cloud_API.Models.Frames)) },
        { "Shot", (EntityType.Shot, typeof(Cellular.Cloud_API.Models.Shot)) }
    };

    private (string? Error, string? Balls, string? Establishments, string? Events, string? Sessions, string? Games, string? Frames, string? Shots)? _cloudCache;
    private DateTime _cloudCacheLoadedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan CloudCacheTtl = TimeSpan.FromSeconds(30);

    public DatabaseVisualizerPage()
    {
        InitializeComponent();
        _db = new CellularDatabase().GetConnection();
        SourcePicker.SelectedIndex = 0;
        LoadTableNames();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (SourcePicker.SelectedIndex < 0)
            SourcePicker.SelectedIndex = 0;
        if (TablePicker.SelectedIndex >= 0)
            LoadTableData(TablePicker.SelectedIndex);
    }

    private void LoadTableNames()
    {
        TablePicker.ItemsSource = new List<string>(_localTables.Keys);
        TablePicker.SelectedIndex = 0;
    }

    private void OnTableSelected(object? sender, EventArgs e)
    {
        if (TablePicker.SelectedIndex < 0) return;
        LoadTableData(TablePicker.SelectedIndex);
    }

    private void OnSourceSelected(object? sender, EventArgs e)
    {
        if (TablePicker.SelectedIndex < 0) return;
        LoadTableData(TablePicker.SelectedIndex);
    }

    private async void OnDeleteLocalDataClicked(object sender, EventArgs e)
    {
        int userId = Preferences.Get("UserId", -1);
        if (userId < 0)
        {
            await DisplayAlert("Delete Local Data", "No signed-in user found.", "OK");
            return;
        }

        bool ok = await DisplayAlert(
            "Delete Local Data",
            "This will delete ALL LOCAL bowling data for this user (Ball/Event/Establishment/Session/Game/Frame/Shot) and keep Users. Continue?",
            "Delete",
            "Cancel");
        if (!ok) return;

        try
        {
            DeleteLocalDataButton.IsEnabled = false;
            DeleteCloudDataButton.IsEnabled = false;
            RowCountLabel.Text = "Deleting local data...";

            var syncService = new CloudSyncService();

            var localErr = await syncService.ClearLocalDataAsync(userId);
            if (localErr != null)
            {
                await DisplayAlert("Local Delete Error", localErr, "OK");
                return;
            }

            // Bust cache and refresh current view.
            _cloudCache = null;
            _cloudCacheLoadedAtUtc = DateTime.MinValue;
            await DisplayAlert("Done", "Local data deleted (users kept).", "OK");
            if (TablePicker.SelectedIndex >= 0)
                LoadTableData(TablePicker.SelectedIndex);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            DeleteLocalDataButton.IsEnabled = true;
            DeleteCloudDataButton.IsEnabled = true;
        }
    }

    private async void OnDeleteCloudDataClicked(object sender, EventArgs e)
    {
        int userId = Preferences.Get("UserId", -1);
        if (userId < 0)
        {
            await DisplayAlert("Delete Cloud Data", "No signed-in user found.", "OK");
            return;
        }

        bool ok = await DisplayAlert(
            "Delete Cloud Data",
            "This will delete ALL CLOUD bowling data for this user (Ball/Event/Establishment/Session/Game/Frame/Shot) and keep Users. Continue?",
            "Delete",
            "Cancel");
        if (!ok) return;

        try
        {
            DeleteLocalDataButton.IsEnabled = false;
            DeleteCloudDataButton.IsEnabled = false;
            RowCountLabel.Text = "Deleting cloud data...";

            var syncService = new CloudSyncService();
            var cloudErr = await syncService.ClearCloudDataAsync(userId);
            if (cloudErr != null)
            {
                await DisplayAlert("Cloud Delete Error", cloudErr, "OK");
                return;
            }

            RowCountLabel.Text = "Running server orphan cleanup...";
            var orphanErr = await syncService.DeleteOrphanedAppDataAsync(userId);

            _cloudCache = null;
            _cloudCacheLoadedAtUtc = DateTime.MinValue;
            var doneMsg = orphanErr == null
                ? "Cloud data deleted (users kept). Orphan cleanup completed."
                : $"Cloud data deleted (users kept). Orphan cleanup failed: {orphanErr}";
            await DisplayAlert("Done", doneMsg, "OK");
            if (TablePicker.SelectedIndex >= 0)
                LoadTableData(TablePicker.SelectedIndex);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            DeleteLocalDataButton.IsEnabled = true;
            DeleteCloudDataButton.IsEnabled = true;
        }
    }

    private async void LoadTableData(int tableIndex)
    {
        var tableNames = new List<string>(_localTables.Keys);
        var tableName = tableNames[tableIndex];
        bool isCloud = SourcePicker.SelectedIndex == 1;

        DataContainer.Children.Clear();
        RowCountLabel.Text = "Loading...";

        try
        {
            if (!isCloud)
            {
                var entityType = _localTables[tableName];
                DeleteInfoLabel.IsVisible = true;
                DeleteInfoLabel.Text = "Tap a row's Delete to remove it from local DB (so local != cloud for testing).";

                var items = await QueryAllAsync(entityType);
                RowCountLabel.Text = $"{tableName} (Local): {items.Count} row(s)";

                for (int i = 0; i < items.Count; i++)
                {
                    var card = BuildRowCard(tableName, i + 1, items[i], showDelete: true);
                    DataContainer.Children.Add(card);
                }
            }
            else
            {
                DeleteInfoLabel.IsVisible = true;
                DeleteInfoLabel.Text = "Cloud view is read-only.";

                if (!_cloudTables.TryGetValue(tableName, out var cloudInfo))
                {
                    RowCountLabel.Text = $"{tableName} (Cloud): not supported";
                    DataContainer.Children.Add(new Label
                    {
                        Text = "This table isn't supported by the cloud viewer yet.",
                        TextColor = Colors.OrangeRed,
                        FontSize = 12,
                        Margin = new Thickness(0, 8)
                    });
                    return;
                }

                var cloud = await EnsureCloudCacheAsync();
                if (cloud.Error != null)
                {
                    RowCountLabel.Text = "Cloud error";
                    DataContainer.Children.Add(new Label
                    {
                        Text = cloud.Error,
                        TextColor = Colors.OrangeRed,
                        FontSize = 12,
                        Margin = new Thickness(0, 8)
                    });
                    return;
                }

                string? json = tableName switch
                {
                    "Ball" => cloud.Balls,
                    "Establishment" => cloud.Establishments,
                    "Event" => cloud.Events,
                    "Session" => cloud.Sessions,
                    "Game" => cloud.Games,
                    "BowlingFrame" => cloud.Frames,
                    "Shot" => cloud.Shots,
                    _ => "[]"
                };

                var items = ParseCloudJsonList(cloudInfo.cloudModelType, json);
                RowCountLabel.Text = $"{tableName} (Cloud): {items.Count} row(s)";

                for (int i = 0; i < items.Count; i++)
                {
                    var card = BuildRowCard(tableName, i + 1, items[i], showDelete: false);
                    DataContainer.Children.Add(card);
                }
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

    private async Task<(string? Error, string? Balls, string? Establishments, string? Events, string? Sessions, string? Games, string? Frames, string? Shots)> EnsureCloudCacheAsync()
    {
        if (_cloudCache != null && DateTime.UtcNow - _cloudCacheLoadedAtUtc < CloudCacheTtl)
            return _cloudCache.Value;

        var syncService = new CloudSyncService();
        var uid = Preferences.Get("UserId", -1);
        _cloudCache = await syncService.FetchCloudDataAsync(syncUserId: uid > 0 ? uid : null);
        _cloudCacheLoadedAtUtc = DateTime.UtcNow;
        return _cloudCache.Value;
    }

    private IReadOnlyList<object> ParseCloudJsonList(Type cloudModelType, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<object>();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = null
        };

        var listType = typeof(List<>).MakeGenericType(cloudModelType);

        object? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize(json, listType, options);
        }
        catch
        {
            // ignore
        }

        if (parsed == null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("data", out var dataEl))
                {
                    parsed = JsonSerializer.Deserialize(dataEl.GetRawText(), listType, options);
                }
            }
            catch
            {
                // ignore
            }
        }

        if (parsed is IList list)
        {
            var objects = new List<object>();
            for (int i = 0; i < list.Count; i++)
                objects.Add(list[i]!);
            return objects;
        }

        return Array.Empty<object>();
    }

    private View BuildRowCard(string tableName, int rowIndex, object row, bool showDelete)
    {
        var props = row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var stack = new VerticalStackLayout { Spacing = 6 };

        var headerRow = new HorizontalStackLayout { Spacing = 8 };
        var header = new Label
        {
            Text = $"{tableName} #{rowIndex}",
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            TextColor = Color.FromArgb("#DFD8F7"),
            VerticalOptions = LayoutOptions.Center
        };
        headerRow.Children.Add(header);

        if (showDelete)
        {
            var deleteBtn = new Button
            {
                Text = "Delete",
                FontSize = 12,
                BackgroundColor = Color.FromArgb("#5C2E2E"),
                TextColor = Colors.White,
                Padding = new Thickness(12, 6)
            };
            deleteBtn.Clicked += async (_, _) =>
            {
                bool ok = await DisplayAlert("Delete row", $"Remove this {tableName} row from local database?", "Delete", "Cancel");
                if (!ok) return;
                try
                {
                    await _db.DeleteAsync(row);
                    RowCountLabel.Text = "Deleted. Refreshing...";
                    LoadTableData(TablePicker.SelectedIndex);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", ex.Message, "OK");
                }
            };
            headerRow.Children.Add(deleteBtn);
        }
        stack.Children.Add(headerRow);

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
