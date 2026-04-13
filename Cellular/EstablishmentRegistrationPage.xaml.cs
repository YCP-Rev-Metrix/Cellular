using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using Cellular.ViewModel;
using Cellular.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SQLite;

namespace Cellular;

public partial class EstablishmentRegistrationPage : ContentPage
{
    private readonly EstablishmentRepository _EstablishmentRepository;
    public ObservableCollection<Establishment> Establishments { get; set; }
    private int userId;
    public EstablishmentRegistrationPage()
    {
        InitializeComponent();
        _EstablishmentRepository = new EstablishmentRepository(new CellularDatabase().GetConnection());
        Establishments = new ObservableCollection<Establishment>();
    }

    // Event handler for the "Register Event" button
    private async void OnRegisterEstablishmentClicked(object sender, EventArgs e)
    {
        // Get the entered data
        string estaName = nameBox.Text;
        string? estaNickname = nickNameBox.Text;
        string? estaLane = lanesBox.Text;
        string? estaType = typeBox.Text;
        string? estaAddress = addressBox.Text;
        string? estaPhone = phoneBox.Text;
        userId = Preferences.Get("UserId", 0);

        Debug.WriteLine("This is USer ID" + userId);

        // Validation to ensure all fields are filled
        if (string.IsNullOrWhiteSpace(estaName))
        {
            await DisplayAlertAsync("Error", "Please enter an Establishment name.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(estaNickname))
        {
            await DisplayAlertAsync("Error", "Please enter an Establishment nickName.", "OK");
            return;
        }

        // You can further process the data here (e.g., save it to a database or display a success message)

        var newEsta = new Establishment
        {
            UserId = userId,
            FullName = estaName,
            NickName = estaNickname,
            Type = estaType,
            HomeHouse = homeHouseBox.IsChecked,
            Lanes = estaLane,
            Address = estaAddress,
            PhoneNumber = estaPhone,
      
        };
        var existingEvent = await _EstablishmentRepository.GetEstablishmentByNameAsync(estaName);
        Debug.WriteLine(existingEvent);
        if (existingEvent == null)
        {
            await _EstablishmentRepository.AddAsync(newEsta);
            Establishments.Add(newEsta);
        }
        await DisplayAlertAsync("Establishment", "The Establishment was Added", "OK");
        // Optionally, clear the form
        nameBox.Text = string.Empty;
        lanesBox.Text = string.Empty;
        typeBox.Text = string.Empty;
        addressBox.Text = string.Empty;

    }
}
