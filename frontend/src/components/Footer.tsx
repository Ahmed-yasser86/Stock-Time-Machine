import { Link } from 'react-router-dom';

export function Footer() {
  return (
    <footer className="border-t border-border">
      <div className="mx-auto flex max-w-6xl flex-col gap-2 px-4 py-6 text-xs text-fg-dim sm:flex-row sm:items-center sm:justify-between">
        <p>
          Stock Time Machine is a historical research instrument — not investment advice.
          Simulations use raw prices; splits and dividends are not factored in.
        </p>
        <p className="flex shrink-0 gap-4">
          <Link to="/methodology" className="underline-offset-4 hover:underline">
            Methodology
          </Link>
          <Link to="/investigate" className="underline-offset-4 hover:underline">
            New investigation
          </Link>
        </p>
      </div>
    </footer>
  );
}
