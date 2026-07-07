namespace MouseKeyProxy.PiHid;

public readonly record struct PiHidResponse(int StatusCode, string Body)
{
    public bool Ok => StatusCode is >= 200 and <= 299;

    public IResult ToResult()
    {
        return Results.Text(Body, "text/plain", statusCode: StatusCode);
    }
}
