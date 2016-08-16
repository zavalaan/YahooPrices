namespace YahooPrices
open Prices

exception GetPriceRangeException of StockGetRangeError
exception GetPriceException of StockGetSingleError

type CSharpInterop() =

    static member GetPriceRangeAsync(symbol, startDate, endDate, adjustForDividends) =
        async {
            let! result = asyncGetRangePrices symbol startDate endDate adjustForDividends
            match result with
            |Choice1Of2 prices -> return prices |> Seq.ofList
            |Choice2Of2 err -> return raise (GetPriceRangeException(err))
        }
        |> Async.StartAsTask

    static member GetPriceAsync(symbol, date) =
        async {
            let! result = asyncGetOnePrice symbol date 
            match result with
            |Choice1Of2 prices -> return prices
            |Choice2Of2 err -> return raise (GetPriceException(err))
        }
        |> Async.StartAsTask

