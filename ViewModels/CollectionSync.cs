using System.Collections.ObjectModel;

namespace Tally.ViewModels;

public static class CollectionSync
{
    /// <summary>
    /// Brings <paramref name="target"/> to match <paramref name="desired"/> with as few individual
    /// changes as possible (remove / move / insert). Unlike Clear()+Add(), this keeps the selection
    /// in a ListBox/ComboBox intact and nothing flickers.
    /// </summary>
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        var wanted = new HashSet<T>(desired);
        for (var i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i])) target.RemoveAt(i);

        var cmp = EqualityComparer<T>.Default;
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && cmp.Equals(target[i], desired[i])) continue;

            var found = -1;
            for (var j = i + 1; j < target.Count; j++)
                if (cmp.Equals(target[j], desired[i])) { found = j; break; }

            if (found >= 0) target.Move(found, i);
            else target.Insert(i, desired[i]);
        }

        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
    }
}
