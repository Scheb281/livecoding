import { useCallback, useEffect, useState } from "react";

export function useResource(api, path) {
  const [state, setState] = useState({ data: null, loading: true, error: "" });
  const [revision, setRevision] = useState(0);
  const refresh = useCallback(() => setRevision((value) => value + 1), []);
  useEffect(() => {
    if (!path) return;
    const controller = new AbortController();
    api
      .request(path, { signal: controller.signal })
      .then((data) => {
        if (!controller.signal.aborted)
          setState({ data, loading: false, error: "" });
      })
      .catch((error) => {
        if (!controller.signal.aborted)
          setState({ data: null, loading: false, error: error.message });
      });
    return () => controller.abort();
  }, [api, path, revision]);
  return { ...state, refresh };
}

export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  async function run(action, success = "Сохранено") {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const data = await action();
      setMessage(success);
      return { data };
    } catch (error) {
      setError(error.message);
      return null;
    } finally {
      setBusy(false);
    }
  }
  return { busy, error, message, run };
}
