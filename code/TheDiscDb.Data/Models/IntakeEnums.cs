namespace TheDiscDb.Web.Data;

public enum IntakeSource
{
    Unknown,
    Engram,
    Web,
    Api,
    Import,
}

public enum IntakeStatus
{
    Pending,
    ReadyForReview,
    Promoted,
    Rejected,
    Partial,
}

public enum IntakeDiscEvidenceType
{
    DiscMetadata,
    ScanLog,
    FrontImage,
    BackImage,
    Other,
}

public enum IntakePromotionStatus
{
    Pending,
    Completed,
    Failed,
    Cancelled,
}

public enum IntakePromotionTarget
{
    Release,
    Disc,
}
