using MandrillSimulator.Models;

namespace MandrillSimulator.Store;

public class ExportJobStore
{
    private readonly Dictionary<string, ExportJob> _jobs = [];
    private readonly Lock _gate = new();

    public void Add(ExportJob job)
    {
        lock (_gate) _jobs[job.Id] = job;
    }

    public ExportJob? Find(string id)
    {
        lock (_gate) return _jobs.TryGetValue(id, out var job) ? job : null;
    }
}
