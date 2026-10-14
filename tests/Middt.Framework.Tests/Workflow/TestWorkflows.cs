namespace Middt.Framework.Tests.Workflow;

/// <summary>Workflow definitions used by the engine, timer, subflow and endpoint tests.</summary>
internal static class TestWorkflows
{
    /// <summary>Pipeline ordering, input handling, roles, $self, finish/cancel.</summary>
    public const string Pipeline = """
        {
          "key": "pipeline",
          "title": "Pipeline",
          "start": {
            "key": "start", "target": "draft",
            "input": {
              "schema": { "type": "object", "additionalProperties": false, "required": [ "name" ],
                          "properties": { "name": { "type": "string", "minLength": 2 }, "amount": { "type": "number", "minimum": 0 } } },
              "set": { "createdBy": "user.Name", "upper": "input.name.ToUpper()" }
            },
            "onExecute": [ { "task": "record", "config": { "name": "start.execute" } } ]
          },
          "states": [
            {
              "key": "draft", "title": "Draft",
              "onEntry": [ { "task": "record", "config": { "name": "draft.entry" } } ],
              "onExit": [ { "task": "record", "config": { "name": "draft.exit" } } ],
              "transitions": [
                { "key": "submit", "title": "Submit", "target": "review",
                  "input": { "schema": { "type": "object", "properties": { "comment": { "type": "string" } } }, "into": "submission" },
                  "onExecute": [ { "task": "record", "config": { "name": "submit.execute" } } ] },
                { "key": "plain", "target": "review" },
                { "key": "touch", "target": "$self",
                  "input": { "schema": { "type": "object", "properties": { "note": { "type": "string" } } }, "into": "notes" },
                  "onExecute": [ { "task": "record", "config": { "name": "touch.execute" } } ] },
                { "key": "approve", "target": "approved", "roles": [ "manager" ] },
                { "key": "auto", "target": "approved", "trigger": "Automatic", "rule": "data.fastTrack == true" }
              ]
            },
            {
              "key": "review", "subType": "Human",
              "onEntry": [
                { "task": "record", "config": { "name": "review.entry" } },
                { "task": "record", "key": "big", "when": "data.amount > 100", "config": { "name": "review.big" } }
              ],
              "transitions": [ { "key": "approve", "target": "approved", "roles": [ "manager" ] } ]
            },
            { "key": "approved", "type": "Finish", "subType": "Success",
              "onEntry": [ { "task": "record", "config": { "name": "approved.entry" } } ] },
            { "key": "cancelled", "type": "Finish", "subType": "Cancelled" }
          ],
          "sharedTransitions": [
            { "key": "cancel", "target": "cancelled", "availableIn": [ "*" ],
              "input": { "schema": { "type": "object", "properties": { "reason": { "type": "string" } } }, "into": "cancellation" } }
          ]
        }
        """;

    /// <summary>Automatic routing by amount; the default branch is listed first but evaluated last.</summary>
    public const string Routing = """
        {
          "key": "routing",
          "start": { "key": "start", "target": "check",
                     "input": { "schema": { "type": "object", "properties": { "amount": { "type": "number" } } } } },
          "states": [
            { "key": "check", "transitions": [
                { "key": "small", "target": "approved", "trigger": "Automatic", "default": true },
                { "key": "big", "target": "review", "trigger": "Automatic", "rule": "data.amount > 5000" },
                { "key": "medium", "target": "medium", "trigger": "Automatic", "rule": "data.amount > 1000" } ] },
            { "key": "medium", "onEntry": [ { "task": "set", "config": { "values": { "routedVia": "\"medium\"" } } } ],
              "transitions": [ { "key": "medium-done", "target": "approved", "trigger": "Automatic", "default": true } ] },
            { "key": "review", "transitions": [ { "key": "approve", "target": "approved" } ] },
            { "key": "approved", "type": "Finish", "subType": "Success" }
          ]
        }
        """;

    public const string Loop = """
        {
          "key": "loop",
          "start": { "key": "start", "target": "ping" },
          "states": [
            { "key": "ping", "transitions": [ { "key": "to-pong", "target": "pong", "trigger": "Automatic", "default": true } ] },
            { "key": "pong", "transitions": [ { "key": "to-ping", "target": "ping", "trigger": "Automatic", "default": true } ] }
          ]
        }
        """;

    public const string Restricted = """
        {
          "key": "restricted", "roles": [ "clerk" ],
          "start": { "key": "start", "target": "open" },
          "states": [
            { "key": "open", "transitions": [ { "key": "close", "target": "closed" } ] },
            { "key": "closed", "type": "Finish" }
          ]
        }
        """;

    /// <summary>Error policies: retry, continue, transition, fault.</summary>
    public const string Errors = """
        {
          "key": "errors",
          "start": { "key": "start", "target": "idle" },
          "states": [
            {
              "key": "idle",
              "transitions": [
                { "key": "retry-ok", "target": "done",
                  "onExecute": [ { "task": "flaky", "key": "flaky-3", "config": { "failTimes": 2 }, "onError": { "maxAttempts": 3, "delay": "00:00:00" } } ] },
                { "key": "retry-exhausted", "target": "done",
                  "onExecute": [ { "task": "flaky", "key": "flaky-5", "config": { "failTimes": 5 }, "onError": { "maxAttempts": 2, "delay": "00:00:00" } } ] },
                { "key": "backoff", "target": "done",
                  "onExecute": [ { "task": "flaky", "key": "flaky-backoff", "config": { "failTimes": 2 }, "onError": { "maxAttempts": 3, "delay": "00:10:00" } } ] },
                { "key": "continue", "target": "done",
                  "onExecute": [
                    { "task": "fail", "key": "optional", "config": { "message": "optional failed" }, "onError": { "action": "Continue" } },
                    { "task": "record", "config": { "name": "after-optional" } } ] },
                { "key": "redirect", "target": "done",
                  "input": { "schema": { "type": "object" }, "into": "redirectInput" },
                  "onExecute": [
                    { "task": "set", "config": { "values": { "beforeFailure": "true" } } },
                    { "task": "fail", "key": "remote", "config": { "message": "remote down" }, "onError": { "action": "Transition", "transition": "to-error" } } ] },
                { "key": "fault", "target": "done",
                  "input": { "schema": { "type": "object", "properties": { "attempt": { "type": "integer" } } }, "into": "faultInput" },
                  "onExecute": [ { "task": "flaky", "key": "fault-once", "config": { "failTimes": 1 } } ] },
                { "key": "fault-entry", "target": "entered" },
                { "key": "to-error", "target": "error-state" }
              ]
            },
            { "key": "entered",
              "onEntry": [ { "task": "set", "config": { "values": { "entered": "true" } } }, { "task": "fail", "key": "entry-fail" } ],
              "transitions": [ { "key": "leave", "target": "done" } ] },
            { "key": "error-state", "onEntry": [ { "task": "record", "config": { "name": "error-state.entry" } } ] },
            { "key": "done", "type": "Finish", "subType": "Success" }
          ]
        }
        """;

    /// <summary>Scheduled transition, workflow timeout.</summary>
    public const string Timers = """
        {
          "key": "timers",
          "timeout": { "after": "P1D", "target": "expired" },
          "start": { "key": "start", "target": "waiting" },
          "states": [
            { "key": "waiting", "transitions": [
                { "key": "escalate", "target": "escalated", "trigger": "Scheduled", "after": "PT10M" },
                { "key": "proceed", "target": "working" },
                { "key": "finish", "target": "done" } ] },
            { "key": "working", "transitions": [ { "key": "back", "target": "waiting" }, { "key": "finish", "target": "done" } ] },
            { "key": "escalated", "onEntry": [ { "task": "record", "config": { "name": "escalated.entry" } } ],
              "transitions": [ { "key": "finish", "target": "done" } ] },
            { "key": "done", "type": "Finish", "subType": "Success" },
            { "key": "expired", "type": "Finish", "subType": "Timeout" }
          ]
        }
        """;

    /// <summary>Child that completes synchronously (automatic chain).</summary>
    public const string ChildAuto = """
        {
          "key": "child-auto",
          "start": { "key": "start", "target": "scoring" },
          "states": [
            { "key": "scoring",
              "onEntry": [ { "task": "set", "config": { "values": { "score": "data.amount > 20000 ? 40 : 85" } } } ],
              "transitions": [
                { "key": "pass", "target": "passed", "trigger": "Automatic", "rule": "data.score >= 50" },
                { "key": "fail", "target": "declined", "trigger": "Automatic", "default": true } ] },
            { "key": "passed", "type": "Finish", "subType": "Success" },
            { "key": "declined", "type": "Finish", "subType": "Error" }
          ]
        }
        """;

    /// <summary>Child that waits for a manual transition.</summary>
    public const string ChildManual = """
        {
          "key": "child-manual",
          "start": { "key": "start", "target": "pending" },
          "states": [
            { "key": "pending", "transitions": [
                { "key": "complete", "target": "completed", "input": { "schema": { "type": "object" }, "into": "answer" } },
                { "key": "decline", "target": "declined" } ] },
            { "key": "completed", "type": "Finish", "subType": "Success" },
            { "key": "declined", "type": "Finish", "subType": "Error" }
          ]
        }
        """;

    public const string ParentSync = """
        {
          "key": "parent-sync",
          "start": { "key": "start", "target": "check",
                     "input": { "schema": { "type": "object", "properties": { "amount": { "type": "number" }, "name": { "type": "string" } } } } },
          "states": [
            { "key": "check", "type": "SubFlow",
              "subFlow": { "workflow": "child-auto", "input": { "amount": "data.amount" }, "outputInto": "credit" },
              "transitions": [
                { "key": "ok", "target": "approved", "trigger": "Automatic", "rule": "child.subType == \"Success\"" },
                { "key": "ko", "target": "rejected", "trigger": "Automatic", "default": true } ] },
            { "key": "approved", "type": "Finish", "subType": "Success" },
            { "key": "rejected", "type": "Finish", "subType": "Error" }
          ]
        }
        """;

    public const string ParentAsync = """
        {
          "key": "parent-async",
          "start": { "key": "start", "target": "waiting-child",
                     "input": { "schema": { "type": "object", "properties": { "name": { "type": "string" } } } } },
          "states": [
            { "key": "waiting-child", "type": "SubFlow",
              "subFlow": { "workflow": "child-manual" },
              "transitions": [
                { "key": "poke", "target": "poked" },
                { "key": "ok", "target": "approved", "trigger": "Automatic", "rule": "child.subType == \"Success\"" },
                { "key": "ko", "target": "rejected", "trigger": "Automatic", "default": true } ] },
            { "key": "poked" },
            { "key": "approved", "type": "Finish", "subType": "Success" },
            { "key": "rejected", "type": "Finish", "subType": "Error" },
            { "key": "cancelled", "type": "Finish", "subType": "Cancelled" }
          ],
          "sharedTransitions": [ { "key": "cancel", "target": "cancelled", "availableIn": [ "*" ] } ]
        }
        """;

    public const string Launcher = """
        {
          "key": "launcher",
          "start": { "key": "start", "target": "launched",
                     "input": { "schema": { "type": "object", "properties": { "orderId": { "type": "integer" } } } },
                     "onExecute": [ { "task": "startWorkflow",
                       "config": { "workflow": "child-manual", "key": "child-{{ data.orderId }}", "data": { "orderId": "{{ data.orderId }}" }, "into": "childId" } } ] },
          "states": [ { "key": "launched", "transitions": [ { "key": "finish", "target": "done" } ] }, { "key": "done", "type": "Finish" } ]
        }
        """;
}
