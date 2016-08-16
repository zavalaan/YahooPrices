#load "Scripts/load-project-debug.fsx" 
open YahooPrices
open System

//Retrieve MSFT price on 8/12
let price = Prices.asyncGetOnePrice "MSFT" (DateTime(2016,8,8)) |> Async.RunSynchronously

//Retrieve all MSFT prices from 8/8 to 8/12
let prices = 
    Prices.asyncGetRangePrices "MSFT" (DateTime(2016,8,8)) (DateTime(2016, 8, 13)) AdjustedForDividendsAndSplits
    |> Async.RunSynchronously
    |> (fun prices -> 
        match prices with
        |Choice1Of2 prices -> prices
        |Choice2Of2 err -> failwith (sprintf "%A" err))

//Prints
// (8/12/2016, 57.939999M)
// (8/11/2016, 58.299999M)
// (8/10/2016, 58.02M)
// (8/9/2016, 58.200001M)
// (8/8/2016, 58.060001M)