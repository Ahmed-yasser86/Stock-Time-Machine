import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Header } from './components/Header';
import { Footer } from './components/Footer';
import Landing from './pages/Landing';
import Methodology from './pages/Methodology';
import Investigate from './pages/Investigate';
import Snapshot from './pages/Snapshot';
// New screen: the 100-day moves investigation lives on its own route.
// No existing route or screen is altered by its presence.
import Moves from './pages/Moves';
import Compare from './pages/Compare';
import { StageNav } from './components/StageNav';

export default function App() {
  return (
    <BrowserRouter>
      <div className="flex min-h-screen flex-col bg-bg text-fg">
        <Header />
        <a
          href="#main-content"
          className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded focus:bg-surface focus:px-3 focus:py-2 focus:text-sm"
        >
          Skip to content
        </a>
        <main
          id="main-content"
          className="surface-grain mx-auto w-full max-w-6xl flex-1 px-4 py-8"
        >
          <StageNav />
          <Routes>
            <Route path="/" element={<Landing />} />
            <Route path="/methodology" element={<Methodology />} />
            <Route path="/investigate" element={<Investigate />} />
            <Route path="/snapshot" element={<Snapshot />} />
            <Route path="/moves" element={<Moves />} />
            <Route path="/compare" element={<Compare />} />
            {/* The legacy status dashboard is retired; the investigation is the product. */}
            <Route path="/dashboard" element={<Navigate to="/investigate" replace />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
        <Footer />
      </div>
    </BrowserRouter>
  );
}
