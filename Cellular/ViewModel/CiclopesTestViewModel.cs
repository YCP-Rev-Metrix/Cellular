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
            VideoKey = "videos/hake_test_2_edited.mp4",
            SdKey = "key"
        };

        var laneBallsTask = controller.ExecuteLaneBallsRunRequest(request);
        var fourDBodyTask = controller.ExecuteFourDBodyRunRequest(request);

        return (laneBallsTask, fourDBodyTask);
    }

    public Task<CiclopesQueryNamesResponse?> GetQueryNamesAsync()
    {
        var controller = new ApiController();
        return controller.ExecuteCiclopesQueryNamesRequest();
    }

    public (Task<LaneBallsQueryResponse?> LaneBallsTask, Task<FourDBodyQueryResponse?> FourDBodyTask) QueryTestAsync(List<string> names)
    {
        var controller = new ApiController();
        var request = new CiclopesQueryRequest { Names = names };
        return (
            controller.ExecuteLaneBallsQueryRequest(request),
            controller.ExecuteFourDBodyQueryRequest(request)
        );
    }
}
