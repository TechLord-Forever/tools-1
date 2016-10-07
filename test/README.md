# Test tools

### WebScreen
Console application to test network route layers. Takes the list of web pages to visit and grabs their screenshots for manual review. Web pages are browsed using mozilla/firefox-base image combined with routes layer image under test.

###### Example 1
Print help
```
WebScreen.exe --help
```

###### Example 2
Take screenshots of 10 the most popular web pages and save them in ./screenshots directory.
```
WebScreen.exe
```

###### Example 3
Take screenshots of web pages from pages.txt file and save them in the specified directory. Use turbobrowsers/block-ad-routes as the routes layer.
```
WebScreen.exe --pagesFile pages.txt --screenshotDir c:\Screenshots --routesLayer turbobrowsers/block-ad-routes
```
