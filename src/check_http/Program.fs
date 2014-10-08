module Intelliplan.Program

open System
open System.Diagnostics
open System.Net
open System.Net.Http

open Nessos.UnionArgParser

// like http://linux.101hacks.com/unix/check-http/ but basic

type CLIArguments =
  | [<Mandatory>] Url of string
  | Expected_Code     of int
  | Expected_String   of string
  | Warning_Time       of int // seconds
  | Critical_Time      of int // seconds
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | Url _              -> "What thing to request"
      | Expected_Code _    -> "Expected HTTP Status Code"
      | Expected_String _  -> "Expected string in response body"
      | Warning_Time _     -> "Response time in seconds for warning state (max 10s)"
      | Critical_Time _    -> "Response time in seconds for critical state (max 10s)"

type ExitCode =
  | OK = 0
  | Warning = 1
  | Critical = 2
  | Unknown = 3

type HttpContent' = string

type Ctx = ArgParseResults<CLIArguments> * TimeSpan * HttpResponseMessage * HttpContent'

type Check =
  abstract test : Ctx -> obj option
  abstract rule : Ctx -> obj -> bool * string
  abstract fail_case : ExitCode

type Check<'a> =
  { test      : Ctx -> 'a option
    rule      : Ctx -> 'a -> bool * string
    fail_case : ExitCode }
  interface Check with
    member x.test ctx = x.test ctx |> Option.map box
    member x.rule ctx o = x.rule ctx (unbox o)
    member x.fail_case = x.fail_case

let rec eval ctx = function
  | [] -> None
  | (check : Check) :: cs ->
    match check.test ctx with
    | Some x ->
      match check.rule ctx x with
      | res, desc ->
        if not res then Some (check.fail_case, desc)
        else eval ctx cs
    | None   -> eval ctx cs

let mk_check test rule fail_case =
  { test = test
    rule = rule
    fail_case = fail_case }
  :> Check

/// returns (ret, ticks)
let time f = async {
  let sw = Stopwatch.StartNew()
  let! ret = f ()
  sw.Stop ()
  return ret, sw.Elapsed
  }

let run (asynk : Async<_ * string>) =
  match asynk |> Async.RunSynchronously with
  | ExitCode.OK, _ -> int ExitCode.OK
  | code, desc ->
    Console.Error.WriteLine desc
    code |> int

[<EntryPoint>]
let main argv =
  let parser  = UnionArgParser.Create<CLIArguments>()
  let results = parser.Parse argv
  use c       = new HttpClient()
  c.Timeout   <- TimeSpan.FromSeconds 10.

  let rules =
    [ mk_check
        (fun (results, _, _, _) -> results.TryPostProcessResult(<@ Expected_Code @>, enum<HttpStatusCode>))
        (fun ((_, _, resp, _) : Ctx) code ->
          resp.StatusCode = code,
          sprintf "expected code '%i %O' but was '%i %O'" (int code) code (int resp.StatusCode) (resp.StatusCode))
        ExitCode.Critical
      mk_check
        (fun (results, _, _, _) -> results.TryGetResult(<@ Expected_String @>))
        (fun (_, _, _, http_content) expected ->
          http_content.Contains(expected),
          sprintf "expected body to contain '%s' but was:\n%s" expected http_content)
        ExitCode.Critical
      mk_check
        (fun (results, _, _, _) -> results.TryPostProcessResult(<@ Critical_Time @>, float >> TimeSpan.FromSeconds))
        (fun (_, dur, _, _) expected ->
          dur <= expected,
          sprintf "critical time threshold (%d s) exceeded; actual %d s" (expected.Seconds) (dur.Seconds))
        ExitCode.Critical
      mk_check
        (fun (results, _, _, _) -> results.TryPostProcessResult(<@ Warning_Time @>, float >> TimeSpan.FromSeconds))
        (fun (_, dur, _, _) expected ->
          dur <= expected,
          sprintf "warning time threshold (%d s) exceeded; actual %d s" (expected.Seconds) (dur.Seconds))
        ExitCode.Warning
    ]

  try
    async {
      let! (r, dur) = time <| fun () -> c.GetAsync(results.GetResult <@ Url @>) |> Async.AwaitTask
      let! contents = r.Content.ReadAsStringAsync() |> Async.AwaitTask
      return eval (results, dur, r, contents) rules |> Option.fold (fun _ t -> t) (ExitCode.OK, "")
    }
    |> run
  with e ->
    Console.Error.Write(e.ToString())
    ExitCode.Unknown |> int