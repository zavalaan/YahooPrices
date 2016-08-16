namespace YahooPrices
open System
open System.Net.Http
open System.Net
open System.Globalization

type Split = {
    Date : DateTime
    SharesOwnedAfterSplit: int
    SharesOwnedBeforeSplit: int
}
    with 

    member x.create (split:Split) =
        split
    ///<summary> Returns the ratio of shares owned after split to shares owned before the split </summary>
    member x.ratio() = 
        (decimal x.SharesOwnedAfterSplit) / (decimal x.SharesOwnedBeforeSplit)

type GetSplitError = 
    |UnknownSymbol
    |Unexpected of HttpResponse

module Splits =
    let getURL symbol (startDate:DateTime) (endDate:DateTime) =
        "http://ichart.finance.yahoo.com/x?" +
        "s=" + symbol +
        "&a=" + Convert.ToString(startDate.Month - 1) +
        "&b=" + Convert.ToString(startDate.Day) +
        "&c=" + Convert.ToString(startDate.Year) +
        "&d=" + Convert.ToString(endDate.Month - 1) +
        "&e=" + Convert.ToString(endDate.Day) +
        "&f=" + Convert.ToString(endDate.Year) +
        "&g=v"; // Inputting parameters to Web URL

    let parseSplit (html:string) =
        let lines = html.Split('\n')
        lines
        |> Array.filter (fun x -> x.Contains("SPLIT"))
        |> Array.map (fun x -> 
            let splitArr = x.Split(',')
            let date = DateTime.ParseExact(splitArr.[1].Trim(),  "yyyyMMdd", CultureInfo.InvariantCulture)
            let splitDataArr = splitArr.[2].Split(':')
            {SharesOwnedAfterSplit = Int32.Parse(splitDataArr.[0]); SharesOwnedBeforeSplit = Int32.Parse(splitDataArr.[1]); Split.Date = date; }
            )

    ///<summary> Retrieves split info for the specified symbol on the given date and 6 days prior. </summary>
    let asyncGetSplitsInRange symbol (startDate:DateTime) (endDate:DateTime) = async {
        let url = getURL symbol startDate endDate
        let client = new HttpClient()
        let! response = client.GetAsync(url) |> Async.AwaitTask
        match response.StatusCode with
        |HttpStatusCode.OK -> 
            let! html = response.Content.ReadAsStringAsync() |>Async.AwaitTask 
            return Choice1Of2 <| parseSplit html
        |HttpStatusCode.NotFound -> return Choice2Of2 UnknownSymbol
        |_ -> return Choice2Of2 <| Unexpected (response.StatusCode, response.Content.ReadAsStringAsync().Result)
        }

