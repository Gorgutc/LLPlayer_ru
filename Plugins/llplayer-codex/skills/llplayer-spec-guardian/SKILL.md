---
name: llplayer-spec-guardian
description: Use when checking that a LLPlayer_ru change matches a stated plan or scope.
---

# LLPlayer Spec Guardian

Compare the work against the requested scope before quality review.

## Checklist

- Does the change stay inside the requested subsystem?
- Did it avoid product code when the task is Codex/tooling only?
- Are Windows/.NET/WPF constraints preserved?
- Are runtime assets and release packaging rules preserved?
- Did it avoid importing web/Node/browser tooling?

Spec gaps are blocking until fixed or explicitly accepted by the user.
