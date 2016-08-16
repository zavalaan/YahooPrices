namespace YahooPrices
open System.Net
open System
open System.Net.Http
open System.Collections.Generic
open System.Threading.Tasks

type HttpResponse = HttpStatusCode * string

type SymbPrice = {
    symbol : string;
    price : decimal;
    }

type StockGetSingleError = 
    |SymbolUnknown
    |NoDataForDate
    |UnexpectedHttpResponse of HttpResponse

type StockGetRangeError = 
    |SymbolUnknown
    |UnexpectedHttpResponse of HttpResponse

type PriceType =
    |AdjustedForDividendsAndSplits
    |NotAdjusted

module Prices =
    
    let private symbolDictionary =
        [
            ("BFB",     "BF-B")
            ("BFB",     "BF-B")
            (@"BF/B ",  "BF-B")
            ("SJRB.TO", "SJRB")
            ("MOGA",    "MOG-A")
            ("TCKB.TO", "TCK-B.TO")
            (@"BRK/B ", "BRK-B")
            (@"BRK.B",  "BRK-B")
        ]
        |> dict
        
    let private fixSymbol (symbol:string) = 
        let mutable symbolFixed = symbol
        symbolFixed <- symbolFixed.Replace(" ", "");
        if (symbolDictionary.ContainsKey(symbol)) then
            symbolFixed <- symbolDictionary.[symbol]
        symbolFixed

    let private getURL symbol date  =
        if date <> DateTime.Today then 
            "http://ichart.finance.yahoo.com/table.csv?" +
                "s=" + fixSymbol symbol +
                "&a=" + Convert.ToString(date.Month - 1) +
                "&b=" + Convert.ToString(date.Day) +
                "&c=" + Convert.ToString(date.Year) +
                "&d=" + Convert.ToString(date.Month - 1) +
                "&e=" + Convert.ToString(date.Day) +
                "&f=" + Convert.ToString(date.Year) +
                "&g=d"; // Inputting parameters to Web URL
        else
            "http://download.finance.yahoo.com/d/quotes.csv?s=" + fixSymbol symbol + "&f=sl1d1t1c1ohgv&e=.csv";
    
    let private getRangeURL (symbol:string) (startDate:DateTime) (endDate:DateTime) = 
        "http://ichart.finance.yahoo.com/table.csv?" +
                "s=" + fixSymbol symbol +
                "&a=" + Convert.ToString(startDate.Month - 1) +
                "&b=" + Convert.ToString(startDate.Day) +
                "&c=" + Convert.ToString(startDate.Year) +
                "&d=" + Convert.ToString(endDate.Month - 1) +
                "&e=" + Convert.ToString(endDate.Day) +
                "&f=" + Convert.ToString(endDate.Year) +
                "&g=d";

    ///<summary>Parses HTML from stock request made on current day and returns the price.
    ///If the date time parsed is not today, returns None.</summary>
    let private parseSingleToday (html:string) =
        let values = html.Split(',')
        let price, date = values.[1].Replace("\"",""), values.[2].Replace("\"", "")
        if(DateTime.Parse(date).Date <> DateTime.Today) then 
            None
        else
            Some (decimal price)

    ///<summary>Parses HTML from stock request made on any day other than today and returns the price
    ///Returns none if there is no historic data on the date provided.</summary>
    let private parseSingleHistoric (html:string) =
            let values = html.Split('\n')
            if values.Length = 2 then None
            else
                values
                |> Seq.skip 1
                |> Seq.map (fun line -> line.Split([|','|]))
                |> Seq.filter(fun values -> values |> Seq.length = 7)
                |> Seq.item 0
                |> (fun x -> Some(decimal x.[6]))

    let private getCsvDataLines (csv:string) =
        csv.Split('\n')
        |>Seq.skip 1
        |>Seq.filter(fun x -> x.Length > 0)
        |>Seq.map(fun x -> x.Split(','))

    let private getDateAndPrice (dataLine:string[]) = 
        Convert.ToDateTime(dataLine.[0]), Convert.ToDecimal(dataLine.[6])

    let private getDateAndPriceNotAdjusted (dataLine:string[]) = 
        Convert.ToDateTime(dataLine.[0]), Convert.ToDecimal(dataLine.[4])

    let private parseRangeHistoric html adjusted = 
        let lineParser =
            match adjusted with
            |NotAdjusted -> getDateAndPriceNotAdjusted
            |AdjustedForDividendsAndSplits -> getDateAndPrice

        getCsvDataLines html
        |>Seq.map lineParser
        |>List.ofSeq

    let asyncGetOnePrice symbol date = async {
        let client = new HttpClient();
        let url = getURL symbol date
        let! httpResponse = Async.AwaitTask (client.GetAsync(url))
        match httpResponse.StatusCode with
        | HttpStatusCode.OK -> 
            let! htmlData = httpResponse.Content.ReadAsStringAsync() |>Async.AwaitTask
            let interpretParsing = function
                |Some price -> Choice1Of2 price
                |None -> Choice2Of2 NoDataForDate
            if date = DateTime.Today then return interpretParsing <| parseSingleToday htmlData
            else return interpretParsing <| parseSingleHistoric htmlData 
        | HttpStatusCode.NotFound -> return Choice2Of2 StockGetSingleError.SymbolUnknown
        | unexpected -> 
            let! responseMessage = httpResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
            return Choice2Of2 <| StockGetSingleError.UnexpectedHttpResponse (unexpected, (responseMessage))
        }

    /// Gets a range of prices from the startDate to the endDate.
    let asyncGetRangePrices symbol startDate endDate priceType = async {
        let url = getRangeURL symbol startDate endDate
        let client = new HttpClient();
        let! httpResponse = client.GetAsync(url) |> Async.AwaitTask
        match httpResponse.StatusCode with
        | HttpStatusCode.OK -> 
            let! stockData = httpResponse.Content.ReadAsStringAsync() |>Async.AwaitTask
            return Choice1Of2 <| parseRangeHistoric stockData priceType
        | HttpStatusCode.NotFound -> return Choice2Of2 StockGetRangeError.SymbolUnknown
        | unexpected -> 
            let! responseMessage = httpResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
            return Choice2Of2 <| StockGetRangeError.UnexpectedHttpResponse (unexpected, (responseMessage))
        }