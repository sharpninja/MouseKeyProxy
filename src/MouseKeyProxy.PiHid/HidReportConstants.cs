namespace MouseKeyProxy.PiHid;

public static class HidReportConstants
{
    public const int KeyboardReportLength = 8;
    public const int MouseReportLength = 4;

    public static readonly byte[] KeyboardNeutralReport = new byte[KeyboardReportLength];
    public static readonly byte[] MouseNeutralReport = new byte[MouseReportLength];
}
