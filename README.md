# YahooPrices

Yahoo finance .NET API library to retrieve stock price information. 

## From C#
```
var prices = CSharpInterop.GetPriceRangeAsync(
    "MSFT",
    new DateTime(2016, 8, 8), new DateTime(2016, 8, 12),
    PriceType.AdjustedForDividendsAndSplits).Result;

foreach (var price in prices)
{
    Console.WriteLine($"{price.Item1}, {price.Item2}");
}
```

## From F#

Retrieve MSFT price on 8/12
```
let price = Prices.asyncGetOnePrice "MSFT" (DateTime(2016,8,8)) |> Async.RunSynchronously
```

Retrieve all MSFT prices from 8/8 to 8/12
```
let prices = 
    Prices.asyncGetRangePrices "MSFT" (DateTime(2016,8,8)) (DateTime(2016, 8, 13)) AdjustedForDividendsAndSplits
    |> Async.RunSynchronously
    |> (fun prices -> 
        match prices with
        |Choice1Of2 prices -> prices
        |Choice2Of2 err -> failwith (sprintf "%A" err))

```
