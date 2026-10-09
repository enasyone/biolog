import type { SyntheticNotification } from '../lib/synthetic-data';

/**
 * Renders the `notification` scenario's derived alerts (BIO-UX-010-AC02).
 * `role="status"` matches the accessible-live-region convention
 * `placeholder-page.tsx`'s own action status already uses — announced to
 * assistive tech without stealing focus, not `role="alert"` (which is
 * reserved for interrupting, time-critical errors).
 */
export function NotificationList({ notifications }: { notifications: readonly SyntheticNotification[] }) {
  if (notifications.length === 0) return null;
  return (
    <section className="biolog-op-notifications" role="status" aria-label="Notifications">
      <h2>Notifications</h2>
      <ul>
        {notifications.map((notification) => (
          <li
            key={notification.id}
            className={`biolog-op-notification biolog-op-notification--${notification.severity}`}
          >
            {notification.message}
          </li>
        ))}
      </ul>
    </section>
  );
}
