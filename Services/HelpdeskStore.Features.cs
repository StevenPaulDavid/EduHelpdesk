using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Switching features off and on (Services/Features.cs).
public sealed partial class HelpdeskStore
{
    public bool IsFeatureOn(string feature) { lock (_sync) return !_data.FeaturesOff.Contains(feature); }
    public IReadOnlySet<string> FeaturesOff { get { lock (_sync) return _data.FeaturesOff.ToHashSet(StringComparer.OrdinalIgnoreCase); } }

    // Called with the lock held, from RoleAllows and RoleHasFlag.
    private bool ModuleSwitchedOff(string module) => Features.ForModule(module) is { } feature && _data.FeaturesOff.Contains(feature);
    private bool FlagSwitchedOff(string flag) => Features.ForFlag(flag) is { } feature && _data.FeaturesOff.Contains(feature);

    // Off takes what needs it off too (loans need assets); on brings what it needs on.
    public (bool Ok, string Message) SetFeature(string? key, bool on)
    {
        lock (_sync)
        {
            if (Features.Find(key) is not { } feature) return (false, "That feature was not found.");
            if (on == !_data.FeaturesOff.Contains(feature.Key)) return (true, $"{feature.Label} {(on ? "is already on" : "is already off")}.");
            var changed = new List<Features.Feature> { feature };
            if (on)
            {
                foreach (var needed in feature.Needs.Select(Features.Find).OfType<Features.Feature>().Where(x => _data.FeaturesOff.Contains(x.Key))) changed.Add(needed);
                foreach (var item in changed) _data.FeaturesOff.Remove(item.Key);
            }
            else
            {
                changed.AddRange(Features.All.Where(x => x.Needs.Contains(feature.Key) && !_data.FeaturesOff.Contains(x.Key)));
                foreach (var item in changed) _data.FeaturesOff.Add(item.Key);
            }
            Save();
            var also = changed.Skip(1).Select(x => x.Label).ToList();
            return (true, on
                ? $"{feature.Label} is on{(also.Count > 0 ? $", and so is {string.Join(" and ", also)}, which it needs" : "")}. Everything it had is back."
                : $"{feature.Label} is off{(also.Count > 0 ? $", and so is {string.Join(" and ", also)}, which needs it" : "")}. Nothing has been deleted: switching it on brings it all back.");
        }
    }
}
