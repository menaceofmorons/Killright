namespace PilotIntel.Core.Style;

public sealed record StyleAnalysisResult(
    StyleClassification GeneralStyle,
    StyleClassification RecentStyle,
    bool GateIndicator);