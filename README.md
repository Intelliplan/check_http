# check_http

A tool from intelliplan for checking the health of a HTTP service from the command line.

## How to build

You build with [albacore][alba].

``` bash
bundle
bundle exec rake
```

## Usage

``` bash
check_http.exe <url> [OPTIONS]

  --url <string>: What thing to request

OPTIONS:

  --expected-code <int>: Expected HTTP Status Code
  --expected-string <string>: Expected string in response body
  --warning-time <int>: Response time in seconds for warning state (max 10s)
  --critical-time <int>: Response time in seconds for critical state (max 10s)
  --help [-h|/h|/help|/?]: display this list of options.
```

Example:

``` bash
$ check_http.exe --url http://intelliplan.se --expected-code 201
expected code '201 Created' but was '200 OK'
$ echo $?
2
```

Outputs zero (0) for OK, one (1) for Warning, two (2) for critical and three (3)
for unknown.

 [alba]: https://github.com/albacore/albacore