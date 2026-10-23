namespace SideDock;
internal sealed class EdgeGesture
{
    bool enteredPanel;
    DateTime since = DateTime.MaxValue;
    int outward;
    DateTime lastMotion;
    public void Reset() { enteredPanel = false; since = DateTime.MaxValue; outward = 0; lastMotion = DateTime.MinValue; }
    public bool Push(int dx, bool right, bool atEdge, bool pinned, bool settled, DateTime now)
    {
        if (!settled || pinned || !atEdge) { outward = 0; lastMotion = now; return false; }
        int delta = right ? dx : -dx;
        if ((now - lastMotion).TotalMilliseconds > 220 || delta < 0) outward = 0;
        lastMotion = now;
        if (delta > 0) outward += delta;
        return outward >= 32;
    }
    public bool ShouldHide(bool inPanel, bool atEdge, bool pinned, DateTime now)
    {
        if (inPanel && !atEdge) enteredPanel = true;
        if (pinned || !enteredPanel || !atEdge) { since = DateTime.MaxValue; return false; }
        if (since == DateTime.MaxValue) since = now;
        return (now - since).TotalMilliseconds >= 160;
    }
}
