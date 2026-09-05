import { useState } from 'react';
import { api } from '../lib/api';
import type { ExplainerResponse } from '../types';
import { Button } from './ui/button';
import { Card, CardContent, CardHeader, CardTitle } from './ui/card';
import { Input } from './ui/input';

/**
 * Grounded methodology Q&A: the backend retrieves the relevant methodology
 * sections deterministically and the model answers ONLY from them. Out-of-
 * scope questions get the exact refusal — never an invented answer.
 */
export function MethodologyExplainer() {
  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState<ExplainerResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  const ask = () => {
    const q = question.trim().slice(0, 500);
    if (!q) return;
    setBusy(true);
    setFailed(false);
    setAnswer(null);
    api
      .explain({ question: q })
      .then((r) => setAnswer(r))
      .catch(() => setFailed(true))
      .finally(() => setBusy(false));
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Ask about the methodology</CardTitle>
        <p className="text-xs text-fg-dim">
          Answered strictly from the sections above — anything outside them gets an explicit
          refusal, not an invention.
        </p>
      </CardHeader>
      <CardContent className="space-y-2">
        <div className="flex flex-wrap gap-2">
          <Input
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') ask();
            }}
            placeholder="e.g. Why is my news window empty?"
            aria-label="Ask about the methodology"
            maxLength={500}
            className="min-w-0 flex-1"
          />
          <Button size="sm" onClick={ask} disabled={busy || question.trim() === ''}>
            {busy ? 'Answering…' : 'Ask'}
          </Button>
        </div>
        {failed && !busy && (
          <p className="text-xs text-fg-dim">
            The explainer is unavailable — the sections above stand on their own.
          </p>
        )}
        {answer && (
          <div className="space-y-1 rounded-md bg-canvas p-2 text-sm">
            <p>{answer.answer}</p>
            {answer.citedSections.length > 0 ? (
              <p className="text-xs text-fg-dim">
                From: {answer.citedSections.join(' · ')}
                {answer.model ? ` · AI-generated (${answer.model})` : ''}
              </p>
            ) : (
              <p className="text-xs text-fg-dim">No methodology section covers this.</p>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
