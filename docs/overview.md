# Overview

Stock Time Machine reconstructs what investors could have known at any past
date — then checks whether the patterns in that information have appeared
before.

## The problem it solves

Financial decisions are judged with hindsight nobody possessed. Analysts,
journalists, and researchers routinely reason from outcomes backward. There
is no widely available instrument that rebuilds the information environment
available up to a defined point-in-time cutoff — prices, filings, news, and
discussion — with proof of what was knowable and what was not. (Corpus gaps
are documented, not hidden; see [limitations](limitations.md).)

## How it works

- Pick a company and a historical date; the system rebuilds everything
  knowable up to 23:59 US/Eastern that day — nothing later can leak in.
- It detects the significant price moves in the prior 100 trading days and
  attaches the evidence available before each one.
- It compares each move's pre-peak information shape against 130 frozen
  historical cases and shows what followed those precedents — as
  description, never as a forecast.

## What it does not do

- It is not investment advice and makes no recommendations.
- It is not a trading system; there is no execution, no alerts, no accounts.
- It does not predict prices. It detects repeating information patterns and
  reports what co-occurred with them in the past.

## Current status

Proof of concept running on real data with real results: 130 frozen cases
across 8 symbols, 449 passing backend tests, live provider integrations
(Alpha Vantage, SEC EDGAR, GDELT, MarketAux, Finnhub) with quota discipline
and honest degradation. Full investigations take 10–20 minutes; the demo
queries in the [README](../README.md) return in seconds against warmed caches.

## Potential applications

- Pre-decision diligence: what was actually visible before a historical
  position date.
- Journalism and research: cited, reproducible reconstructions of what was
  knowable when.
- Compliance and training: demonstrating information boundaries with proof
  rather than assertion.
- Pattern libraries: accumulating observable pre-event conditions across
  companies and cycles.
