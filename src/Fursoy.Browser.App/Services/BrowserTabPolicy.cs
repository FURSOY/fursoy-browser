namespace Fursoy.Browser.App.Services;

public static class BrowserTabPolicy
{
    public static int CalculateOpenerInsertionIndex(
        IReadOnlyList<Guid?> openerIds,
        int openerIndex,
        Guid openerId,
        int pinnedCount)
    {
        if (openerIndex < 0 || openerIndex >= openerIds.Count) return openerIds.Count;

        var insertionIndex = openerIndex + 1;
        while (insertionIndex < openerIds.Count && openerIds[insertionIndex] == openerId)
            insertionIndex++;

        return Math.Clamp(Math.Max(insertionIndex, pinnedCount), 0, openerIds.Count);
    }

    public static int CalculateInsertionIndex(double draggedCenter, IEnumerable<double> otherTabCenters) =>
        otherTabCenters.Count(center => draggedCenter > center);

    public static int CalculateNearestSlotIndex(double draggedCenter, IReadOnlyList<double> slotCenters)
    {
        if (slotCenters.Count == 0) return 0;
        var bestIndex = 0;
        var bestDistance = Math.Abs(draggedCenter - slotCenters[0]);
        for (var index = 1; index < slotCenters.Count; index++)
        {
            var distance = Math.Abs(draggedCenter - slotCenters[index]);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestIndex = index;
        }

        return bestIndex;
    }

    public static int CalculateDragSlotIndex(
        double draggedCenter,
        IReadOnlyList<double> slotCenters,
        int originalIndex)
    {
        if (slotCenters.Count == 0) return 0;
        originalIndex = Math.Clamp(originalIndex, 0, slotCenters.Count - 1);
        var result = originalIndex;

        if (draggedCenter >= slotCenters[originalIndex])
        {
            for (var index = originalIndex + 1; index < slotCenters.Count; index++)
            {
                var threshold = (slotCenters[index - 1] + slotCenters[index]) / 2;
                if (draggedCenter < threshold) break;
                result = index;
            }
        }
        else
        {
            for (var index = originalIndex - 1; index >= 0; index--)
            {
                var threshold = (slotCenters[index] + slotCenters[index + 1]) / 2;
                if (draggedCenter > threshold) break;
                result = index;
            }
        }

        return result;
    }

    public static double GetInternalGapOffset(
        int tabIndex,
        int originalIndex,
        int dropIndex,
        double slotWidth)
    {
        if (dropIndex > originalIndex && tabIndex > originalIndex && tabIndex <= dropIndex)
            return -slotWidth;
        if (dropIndex < originalIndex && tabIndex >= dropIndex && tabIndex < originalIndex)
            return slotWidth;
        return 0;
    }

    public static double CalculateMaximumRightDragOffset(
        double baseLeft,
        double tabWidth,
        double tabAreaRight,
        double trailingButtonSpace)
    {
        var maximumLeft = Math.Max(0, tabAreaRight - tabWidth - trailingButtonSpace);
        return Math.Max(-baseLeft, maximumLeft - baseLeft);
    }

    public static double CalculateTrailingButtonOffset(
        double draggedLeft,
        double tabWidth,
        double buttonBaseLeft,
        double buttonWidth,
        double tabAreaRight,
        bool draggedTabTargetsLastSlot)
    {
        if (!draggedTabTargetsLastSlot) return 0;
        var desiredOffset = Math.Max(0, draggedLeft + tabWidth + 4 - buttonBaseLeft);
        var maximumOffset = Math.Max(0, tabAreaRight - buttonBaseLeft - buttonWidth);
        return Math.Min(desiredOffset, maximumOffset);
    }

    public static int ConstrainInsertionIndex(int requestedIndex, int tabCount, int pinnedCount, bool isPinned)
    {
        var safeTabCount = Math.Max(0, tabCount);
        var safePinnedCount = Math.Clamp(pinnedCount, 0, safeTabCount);
        return isPinned
            ? Math.Clamp(requestedIndex, 0, safePinnedCount)
            : Math.Clamp(requestedIndex, safePinnedCount, safeTabCount);
    }

    public static bool ShouldSuspend(
        bool isActive,
        bool isPinned,
        bool isAudible,
        bool isLoading,
        bool isAttached,
        DateTime lastActivatedUtc,
        DateTime nowUtc,
        TimeSpan idleThreshold)
    {
        return isAttached &&
               !isActive &&
               !isPinned &&
               !isAudible &&
               !isLoading &&
               lastActivatedUtc <= nowUtc - idleThreshold;
    }
}
