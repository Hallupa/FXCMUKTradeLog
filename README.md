# FXCM UK TradeLog

## FXCM UK TradeLog is a trade logger for FXCM UK, importing trades using FXCM's API and providing analysis.

The application imports trades directly from FXCM UK - once imported, trades can be:
* Viewed on a chart
* Annotated
* Allocated to a strategy
* Analysed based on success rate, expectancy, R multiple, etc for all trades/monthly/per strategy/etc

# Installation
1. Download latest version from https://github.com/Hallupa/FXCMUKTradeLog/releases
2. Run 'TradeLogInstaller.msi' which will install the application along with shortcuts on the desktop and start menu. The application is called 'FXCM UK Trade Log'

# How to use
When TradeLog is open, for most actions to work you will need to be logged into your FXCM account. Click 'Login' to do this (Note that TradeLog does not store the password for security)
## Update trades
Once logged-in, clicking 'Update account' will download all FXCM trades. This may take a few minutes as candle prices may also be downloaded so the GBP/point can also be calculated.
## Summary screen
![Screenshot](https://github.com/Hallupa/FXCMUKTradeLog/blob/master/Docs/Images/SummaryScreen.png)
The summary screen displays completed trades' profits, total profit over time from completed trades, etc.
## Trades screen
![Screenshot](https://github.com/Hallupa/FXCMUKTradeLog/blob/master/Docs/Images/ResultsScreen.png)
Trades are grouped here to show results. Using the dropdown list, they can be grouped by month, strategy, market, etc.
## Results screen
![Screenshot](https://github.com/Hallupa/FXCMUKTradeLog/blob/master/Docs/Images/TradesScreen.png)

# Limitations
The application uses the FXCM API to import user trades but this API does have some limitations
## Closed trades
FXCM API doesn't provide details of stops/limits for closed trades.
It is recommended that trades are imported into the trade log when they are still open so their stops/limits can be imported also.
## Open trades
This application can show all the changes to the stop/limit over time for each trade however FXCM API provides only the current stop/limit for open trades.
It is recommended that whenever a trade has its stop/limit changed, TradeLog is updated with the FXCM account to ensure every stop/limit change is imported
