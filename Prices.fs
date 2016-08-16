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
    
    let private symbolDictionary = new Dictionary<string,string>()

    let private initializeDictionary (dict:Dictionary<string,string>) = 
        let add = dict.Add
        add ("BFB",     "BF-B")
        add (@"BF/B ",  "BF-B")
        add ("SJRB.TO", "SJRB")
        add ("MOGA",    "MOG-A")
        add ("TCKB.TO", "TCK-B.TO")
        add (@"BRK/B ", "BRK-B")
        add (@"BRK.B",  "BRK-B")
    do
        initializeDictionary symbolDictionary
        
    let private symbolFixer (symbol:string) = 
        let mutable symbolFixed = symbol
        symbolFixed <- symbolFixed.Replace(" ", "");
        if (symbolDictionary.ContainsKey(symbol)) then
            symbolFixed <- symbolDictionary.[symbol]
        symbolFixed

    /// Generates yahoo finance URL from given symbol and date
    let private getURL symbol date  =
        if date <> DateTime.Today then 
            "http://ichart.finance.yahoo.com/table.csv?" +
                "s=" + symbolFixer symbol +
                "&a=" + Convert.ToString(date.Month - 1) +
                "&b=" + Convert.ToString(date.Day) +
                "&c=" + Convert.ToString(date.Year) +
                "&d=" + Convert.ToString(date.Month - 1) +
                "&e=" + Convert.ToString(date.Day) +
                "&f=" + Convert.ToString(date.Year) +
                "&g=d"; // Inputting parameters to Web URL
        else
            "http://download.finance.yahoo.com/d/quotes.csv?s=" + symbolFixer symbol + "&f=sl1d1t1c1ohgv&e=.csv";
    
    let private getRangeURL (symbol:string) (startDate:DateTime) (endDate:DateTime) = 
        "http://ichart.finance.yahoo.com/table.csv?" +
                "s=" + symbolFixer symbol +
                "&a=" + Convert.ToString(startDate.Month - 1) +
                "&b=" + Convert.ToString(startDate.Day) +
                "&c=" + Convert.ToString(startDate.Year) +
                "&d=" + Convert.ToString(endDate.Month - 1) +
                "&e=" + Convert.ToString(endDate.Day) +
                "&f=" + Convert.ToString(endDate.Year) +
                "&g=d";

    ///Parses HTML from stock request made on current day and returns the price.
    ///If the date time parsed is not today, returns None.
    let private parseSingleToday (html:string) =
        let values = html.Split(',')
        let price, date = values.[1].Replace("\"",""), values.[2].Replace("\"", "")
        if(DateTime.Parse(date).Date <> DateTime.Today) then 
            None
        else
            Some (decimal price)

    ///Parses HTML from stock request made on any day other than today and returns the price
    ///Returns none if there is no historic data on the date provided.
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

    let getCsvDataLines (csv:string) =
        csv.Split('\n')
        |>Seq.skip 1
        |>Seq.filter(fun x -> x.Length > 0)
        |>Seq.map(fun x -> x.Split(','))

    let getDateAndPrice (dataLine:string[]) = 
        Convert.ToDateTime(dataLine.[0]), Convert.ToDecimal(dataLine.[6])

    let getDateAndPriceNotAdjusted (dataLine:string[]) = 
        Convert.ToDateTime(dataLine.[0]), Convert.ToDecimal(dataLine.[4])

    let private parseRangeHistoric html adjusted = 
        let lineParser =
            match adjusted with
            |NotAdjusted -> getDateAndPriceNotAdjusted
            |AdjustedForDividendsAndSplits -> getDateAndPrice

        getCsvDataLines html
        |>Seq.map lineParser
        |>List.ofSeq

    let private parseRangeHistoricNotAdjusted html = 
        getCsvDataLines html
        |>Seq.map getDateAndPriceNotAdjusted
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

    ///Retrieves the price for a stock on a given date. If unable to retrieve the price, goes back up to 5 days to get the most recent price
    /// available. If unable to find a price for the given symbol, returns -1 for price.
    let internal asyncGetClosestPrice symbol (date:DateTime) = async {
        let rec inner symbol (changingDate:DateTime) counter = async {
            if counter = 5 then 
                return Choice2Of2 StockGetSingleError.SymbolUnknown
            else
                let! result = asyncGetOnePrice symbol changingDate
                match result with
                | Choice1Of2 lot -> return Choice1Of2 lot
                | Choice2Of2 error -> 
                    match error with
                    |StockGetSingleError.NoDataForDate -> return! (inner symbol (changingDate.AddDays(-1.0)) (counter + 1)) 
                    |StockGetSingleError.SymbolUnknown -> return Choice2Of2 StockGetSingleError.SymbolUnknown
                    |StockGetSingleError.UnexpectedHttpResponse response -> return Choice2Of2 (StockGetSingleError.UnexpectedHttpResponse response)
            }
        return! inner symbol date 0 
        }
   
    /// Gets a range of prices from the startDate to the endDate.
    let asyncGetRangePrices symbol startDate endDate priceType = async {
        let priceType' = defaultArg priceType PriceType.AdjustedForDividendsAndSplits
        let url = getRangeURL symbol startDate endDate
        let client = new HttpClient();
        let! httpResponse = client.GetAsync(url) |> Async.AwaitTask
        match httpResponse.StatusCode with
        | HttpStatusCode.OK -> 
            let! stockData = httpResponse.Content.ReadAsStringAsync() |>Async.AwaitTask
            return Choice1Of2 <| parseRangeHistoric stockData priceType'
        | HttpStatusCode.NotFound -> return Choice2Of2 StockGetRangeError.SymbolUnknown
        | unexpected -> 
            let! responseMessage = httpResponse.Content.ReadAsStringAsync() |> Async.AwaitTask
            return Choice2Of2 <| StockGetRangeError.UnexpectedHttpResponse (unexpected, (responseMessage))
        }