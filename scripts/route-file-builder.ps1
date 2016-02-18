# Licensed under the Apache License, Version 2.0
# http://www.apache.org/licenses/LICENSE-2.0

<#

.SYNOPSIS

Tool to help build a txt file to be used as input for --route-file parameter to new/run/start/commit commands.

.DESCRIPTION

This cmdlet will run a browser with the specified urls allowing you to test if all the necessary hosts/ips are unblocked.

If you see that the site(s) don't load or render properly, then simply exit the browser and answer "no" at the prompt. The browser will launch again with additional settings. Repeat this process until everything appears to work.

Use the output file with the turbo new/run/start/commmit command via the --route-file= flag...

> turbo new firefox --route-block=ip --route-file=c:\path\to\routes.txt

Requires the Turbo.net client to be installed.

.PARAMETER urls

An array of urls that need to be tested.

.PARAMETER routesFile

A path to a file which will recieve the routes file data. If not specified, the data will be written to the console and can be redirect to a file at that time.

#>

[CmdletBinding()]
param
(
    [Parameter(Mandatory=$True,ValueFromPipeline=$False,ValueFromPipelineByPropertyName=$False,HelpMessage="The starter urls")]
    [string[]] $urls,
    [Parameter(Mandatory=$False,ValueFromPipeline=$False,ValueFromPipelineByPropertyName=$False,HelpMessage="The file to recieve the routes information")]
    [string] $routesFile
)

# returns a list of blocked hosts/ips to unblock.
function GetBlocked([string]$container) {
    # find all the network logs for the container
    $logsDir = Join-Path -path $env:LOCALAPPDATA -ChildPath "spoon\containers\sandboxes\$container\logs"
    $logs = Get-ChildItem $logsDir | 
                Where { $_.Name.StartsWith("xcnetwork_") } | 
                Select @{Name="path"; Expression={ Join-Path -path $logsDir -childpath $_.Name }} | 
                Select -ExpandProperty path


    $hostmap = @{}
    $blocked = @()
    ForEach($log in $logs) {
        $lines = Get-Content $log

        ForEach($line in $lines) {
            if($line -match 'Host (.*) resolved to: (.*)') { 
                # keep track of host->ip mappings that we encounter
                $hostname = $matches[1]
                $ip = $matches[2]
                if($ip -match '::ffff:(\d+\.\d+\.\d+\.\d+)') {
                    # parse ipv6 mapped ipv4
                    $ip = $matches[1] 
                }
                $hostmap.set_item($ip, $hostname)
            }
            elseif($line -match 'Connection blocked: (.*)') {
                # track blocked connections
                $ip = $matches[1]
                if($hostmap.ContainsKey($ip)) {
                    # resolve the ip to a host if we know what it is
                    $ip = $hostmap."$ip"
                }
                $blocked += ,$ip
            }
        }
    }

    $blocked | select -Unique
}

# writes a route file based on the list of blocked ip/hosts. if the route file already exists, then the new blocked entries are merged in.
function BuildRouteFile([string]$routeFile, [string[]]$unblock, [string[]]$block) {
    
    $routes = @{}

    # read in the existing route file
    if(Test-Path $routeFile) {
        $lines = Get-Content $routeFile

        $section = ""
        $list = @()
        ForEach ($line in $lines) {
            if($line -match '\[(.*)\]') {
                if($section) {
                    $routes.add($section, $list)
                    $list = @()
                }
                $section = $matches[1]
            }
            elseif($line.Length -gt 0) {
                $list += ,$line
            }
        }
        if($section) {
            $routes.add($section, $list)
        }

        # clear previous content 
        Clear-Content $routeFile
    }

    # merge
    if($unblock) {
        MergeRouteFileSection $routes "ip-add" $unblock
    }
    if($block) {
        MergeRouteFileSection $routes "ip-block" $block
    }
    
    # write new file
    ForEach ($section in $routes.Keys) {
        Add-Content $routeFile "[$section]"
        $list = $routes."$section"
        ForEach ($ip in $list) {
            Add-Content $routeFile $ip
        }
        Add-Content $routeFile "`n"
    }
}

function MergeRouteFileSection([hashtable]$routes, [string]$section, [string[]]$new) {

    if($routes.ContainsKey($section)) {
        $list = $routes."$section"
    }
    $new.ForEach({$list += ,$_})
    $list = $list | select -Unique
    $routes.set_item($section, $list)
}

# runs a browser with the defined routes. returns the container id.
function RunBrowser([string]$urls, [string]$routesFile, [string]$containerToResume, [string]$browser = "firefox") {
    if(-not $containerToResume) {
        $params = ,"new $browser"
    }
    else {
        $params = ,"start $containerToResume"
    }

    $params += ,"--format=json"
    $params += ,"--diagnostic"

    $params += ,"--route-block=ip" # todo: need to be able to put this block rule in the file itself
    $params += ,"--route-file=`"$routesFile`""

    $params += ,"--"
    ForEach ($url in $urls) {
        $params += ,"$url"
    }

    try {
        $tempFile = [System.IO.Path]::GetTempFileName()
        Start-Process -FilePath "turbo" -ArgumentList $params -Wait -RedirectStandardOutput $tempFile -WindowStyle Hidden
        $ret = Get-Content $tempFile | ConvertFrom-Json
    }
    finally {
        # clean up
        Remove-Item $tempFile
    }

    $ret.result.container.id
}





# use temp file if we didn't specify one
$tempFile = ""
if(-not $routesFile) {
    $tempFile = [System.IO.Path]::GetTempFileName()
    $routesFile = $tempFile
}

try {
    # initialize routes file
    $routesToAdd = @()
    ForEach ($url in $urls) {
        $hostname = ([System.Uri]$url).Host -replace '^www\.'
        $routesToAdd += ,"*.$hostname"
    }

    BuildRouteFile $routesFile $routesToAdd

    # loop until we find everything we need to unblock
    $container = ""
    $continue = "n"
    while(-not $continue.StartsWith("y")) {
        Write-Host "Running browser..."
        $container = RunBrowser $urls $routesFile $container

        $blocked = GetBlocked $container
        $routesToAdd += $blocked

        BuildRouteFile $routesFile $routesToAdd

        $continue = (Read-Host -Prompt "Did everything work correctly? (y/n)").ToLower()
    }

    # output temp file to console if we didn't have a file specified
    if($tempFile) {
        Get-Content $tempFile
    }
}
finally {
    # clean up
    if($tempFile) {
        Remove-Item $tempFile
    }
}