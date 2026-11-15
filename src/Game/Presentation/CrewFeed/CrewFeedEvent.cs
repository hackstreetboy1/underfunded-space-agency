namespace Usa.Game.Presentation.CrewFeed;

/// <summary>Moments the feed reacts to: an expression hold and/or a gesture.</summary>
public enum CrewFeedEvent
{
    None,

    /// <summary>A warning or master alarm went off: alarmed face for a moment.</summary>
    Alarm,

    /// <summary>The trouble is over: relief gesture and relieved face.</summary>
    Relief,

    /// <summary>First radio contact of the mission: wave.</summary>
    FirstContact,

    /// <summary>An order addressed to this crew member was accepted: nod.</summary>
    Acknowledge,
}
