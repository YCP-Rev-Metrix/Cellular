using Cellular.Cloud_API;
using Cellular.Cloud_API.Models;

namespace Cellular.ViewModel;

public class CiclopesTestViewModel
{
    public (Task<LaneBallsRunResponse?> LaneBallsTask, Task<FourDBodyRunResponse?> FourDBodyTask) RunTestAsync()
    {
        var controller = new ApiController();
        var request = new CiclopesRunRequest
        {
            VideoKey = "videos/310fceda-dac8-4bf0-a25c-2d1ba360ea68_shot1.mp4",
            SdKey = "key"
        };

        var laneBallsTask = controller.ExecuteLaneBallsRunRequest(request);
        var fourDBodyTask = controller.ExecuteFourDBodyRunRequest(request);

        return (laneBallsTask, fourDBodyTask);
    }

    public (Task<LaneBallsQueryResponse?> LaneBallsTask, Task<FourDBodyQueryResponse?> FourDBodyTask) QueryTestAsync(List<int> shotNumbers)
    {
        var controller = new ApiController();
        var request = new CiclopesQueryRequest { ShotNumbers = shotNumbers };
        return (
            controller.ExecuteLaneBallsQueryRequest(request),
            controller.ExecuteFourDBodyQueryRequest(request)
        );
    }
}
