import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Header } from './components/Header';
import { Footer } from './components/Footer';
import Landing from './pages/Landing';
import Methodology from './pages/Methodology';
import Investigate from './pages/Investigate';
import Snapshot from './pages/Snapshot';

export default function App() {
  return (
    <BrowserRouter>
      <div className="flex min-h-screen flex-col bg-bg text-fg">
        <Header />
        <main className="surface-grain mx-auto w-full max-w-6xl flex-1 px-4 py-8">
          <Routes>
            <Route path="/" element={<Landing />} />
            <Route path="/methodology" element={<Methodology />} />
            <Route path="/investigate" element={<Investigate />} />
            <Route path="/snapshot" element={<Snapshot />} />
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
